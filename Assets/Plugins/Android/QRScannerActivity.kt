package com.unity.template.ar_mobile

import android.Manifest
import android.content.pm.PackageManager
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.PorterDuff
import android.graphics.PorterDuffXfermode
import android.graphics.RectF
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.os.Bundle
import android.util.Size
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.FrameLayout
import android.widget.TextView
import androidx.activity.OnBackPressedCallback
import androidx.appcompat.app.AppCompatActivity
import androidx.camera.core.*
import androidx.camera.lifecycle.ProcessCameraProvider
import androidx.camera.view.PreviewView
import androidx.core.app.ActivityCompat
import androidx.core.content.ContextCompat
import com.google.mlkit.vision.barcode.BarcodeScanning
import com.google.mlkit.vision.common.InputImage
import com.unity3d.player.UnityPlayer
import java.util.concurrent.ExecutorService
import java.util.concurrent.Executors

/**
 * Нативный экран сканирования QR-кодов для Android.
 * Камера и распознавание работают средствами CameraX и ML Kit,
 * а найденный код возвращается в Unity через UnitySendMessage.
 */
class QRScannerActivity : AppCompatActivity() {
    private lateinit var previewView: PreviewView
    // Анализ камеры выполняется в отдельном потоке, чтобы не блокировать интерфейс.
    private lateinit var cameraExecutor: ExecutorService
    // Флаг читается из потока анализа и меняется при закрытии экрана или находке кода.
    @Volatile private var isScanning = true
    // Не позволяет отправить Unity несколько результатов с соседних кадров.
    private var resultSent = false

    companion object {
        // Идентификатор запроса, с которым Android возвращает решение по разрешению камеры.
        private const val REQUEST_CODE_PERMISSIONS = 10
        private val REQUIRED_PERMISSIONS = arrayOf(Manifest.permission.CAMERA)
        // Unity-объект и публичный метод, принимающие результат нативного сканера.
        private const val UNITY_OBJECT = "QRManager"
        private const val UNITY_METHOD = "OnNativeQRScanned"
        private const val COLOR_CYAN = 0xFF35D7F2.toInt()
    }

    /** Создаёт экран сканера, его подсказки и запускает камеру после проверки доступа. */
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        // PreviewView показывает изображение задней камеры на весь экран.
        previewView = PreviewView(this).apply {
            layoutParams = ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.MATCH_PARENT
            )
            scaleType = PreviewView.ScaleType.FILL_CENTER
        }

        // Поверх превью размещаются рамка сканирования и элементы управления.
        val root = FrameLayout(this)
        root.setBackgroundColor(Color.BLACK)
        root.addView(previewView)

        val guide = ScanFrameView(this)
        root.addView(guide, FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT,
            ViewGroup.LayoutParams.MATCH_PARENT
        ))

        // Кнопка закрывает нативный экран и сообщает Unity, что сканирование отменено.
        val backButton = TextView(this).apply {
            text = "‹  Назад"
            textSize = 16f
            setTextColor(Color.WHITE)
            gravity = Gravity.CENTER
            contentDescription = "Закрыть сканер и вернуться в приложение"
            background = roundedBackground(0xCC102235.toInt(), dp(22).toFloat())
            setPadding(dp(18), dp(10), dp(18), dp(10))
            setOnClickListener { finishWithResult("CANCELLED") }
        }
        val backParams = FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.WRAP_CONTENT,
            dp(48),
            Gravity.TOP or Gravity.START
        ).apply { setMargins(dp(20), dp(22), dp(20), 0) }
        root.addView(backButton, backParams)

        // Заголовок, подзаголовок и подсказка помогают навести камеру на код коробки.
        val title = TextView(this).apply {
            text = "Сканирование QR-кода"
            textSize = 23f
            typeface = Typeface.create("sans-serif", Typeface.BOLD)
            setTextColor(Color.WHITE)
            gravity = Gravity.CENTER
            setShadowLayer(8f, 0f, 2f, Color.BLACK)
        }
        val titleParams = FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT,
            ViewGroup.LayoutParams.WRAP_CONTENT,
            Gravity.TOP or Gravity.CENTER_HORIZONTAL
        ).apply { setMargins(dp(24), dp(90), dp(24), 0) }
        root.addView(title, titleParams)

        val subtitle = TextView(this).apply {
            text = "Наведите рамку на QR-код коробки"
            textSize = 16f
            setTextColor(0xFFEAF8FF.toInt())
            gravity = Gravity.CENTER
            setShadowLayer(6f, 0f, 2f, Color.BLACK)
        }
        val subtitleParams = FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT,
            ViewGroup.LayoutParams.WRAP_CONTENT,
            Gravity.TOP or Gravity.CENTER_HORIZONTAL
        ).apply { setMargins(dp(28), dp(130), dp(28), 0) }
        root.addView(subtitle, subtitleParams)

        val hint = TextView(this).apply {
            text = "Код распознается автоматически\nДержите его внутри рамки"
            textSize = 16f
            setTextColor(Color.WHITE)
            gravity = Gravity.CENTER
            background = roundedBackground(0xCC102235.toInt(), dp(18).toFloat())
            setPadding(dp(22), dp(14), dp(22), dp(14))
        }
        val hintParams = FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.WRAP_CONTENT,
            ViewGroup.LayoutParams.WRAP_CONTENT,
            Gravity.BOTTOM or Gravity.CENTER_HORIZONTAL
        ).apply { setMargins(dp(24), 0, dp(24), dp(54)) }
        root.addView(hint, hintParams)

        setContentView(root)
        // Единственный рабочий поток для анализа кадров CameraX.
        cameraExecutor = Executors.newSingleThreadExecutor()
        // Системная кнопка «Назад» завершает сканирование тем же путём, что и кнопка на экране.
        onBackPressedDispatcher.addCallback(this, object : OnBackPressedCallback(true) {
            override fun handleOnBackPressed() {
                finishWithResult("CANCELLED")
            }
        })

        // Запуск камеры откладывается до получения разрешения пользователя.
        if (allPermissionsGranted()) {
            startCamera()
        } else {
            ActivityCompat.requestPermissions(this, REQUIRED_PERMISSIONS, REQUEST_CODE_PERMISSIONS)
        }
    }

    /** Связывает превью и анализ кадров с жизненным циклом этого Activity. */
    private fun startCamera() {
        val cameraProviderFuture = ProcessCameraProvider.getInstance(this)
        cameraProviderFuture.addListener({
            try {
                val cameraProvider = cameraProviderFuture.get()
                // Отображаем поток с задней камеры в PreviewView.
                val preview = Preview.Builder().build().also {
                    it.setSurfaceProvider(previewView.surfaceProvider)
                }

                // Берём только последний кадр, чтобы очередь анализа не отставала от камеры.
                val imageAnalysis = ImageAnalysis.Builder()
                    .setTargetResolution(Size(1280, 720))
                    .setBackpressureStrategy(ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST)
                    .build()
                    .also {
                        it.setAnalyzer(cameraExecutor) { imageProxy ->
                            if (!isScanning) {
                                imageProxy.close()
                                return@setAnalyzer
                            }
                            processImage(imageProxy)
                        }
                    }

                // Перед привязкой снимаем старые сценарии использования камеры.
                cameraProvider.unbindAll()
                cameraProvider.bindToLifecycle(
                    this,
                    CameraSelector.DEFAULT_BACK_CAMERA,
                    preview,
                    imageAnalysis
                )
            } catch (e: Exception) {
                finishWithResult("ERROR:${e.message}")
            }
        }, ContextCompat.getMainExecutor(this))
    }

    /** Передаёт кадр в ML Kit и обязательно освобождает его после обработки. */
    private fun processImage(imageProxy: ImageProxy) {
        val mediaImage = imageProxy.image ?: run {
            imageProxy.close()
            return
        }

        // rotationDegrees сообщает ML Kit, как повернуть кадр для корректного распознавания.
        val image = InputImage.fromMediaImage(mediaImage, imageProxy.imageInfo.rotationDegrees)
        BarcodeScanning.getClient().process(image)
            .addOnSuccessListener { barcodes ->
                // Отправляем первое найденное значение QR-кода; дальнейшая проверка выполняется в Unity.
                val value = barcodes.firstNotNullOfOrNull { it.rawValue }
                if (value != null && isScanning) {
                    finishWithResult(value)
                }
            }
            .addOnFailureListener { error ->
                android.util.Log.w("QRScanner", "QR recognition failed", error)
            }
            .addOnCompleteListener { imageProxy.close() }
    }

    /** Однократно останавливает сканирование, возвращает результат и закрывает Activity. */
    private fun finishWithResult(message: String) {
        if (resultSent) return
        resultSent = true
        isScanning = false
        sendToUnity(message)
        runOnUiThread { finish() }
    }

    /** Передаёт строку Unity-методу QRManager.OnNativeQRScanned. */
    private fun sendToUnity(message: String) {
        UnityPlayer.UnitySendMessage(UNITY_OBJECT, UNITY_METHOD, message)
    }

    /** Проверяет, выданы ли приложению все разрешения, нужные сканеру. */
    private fun allPermissionsGranted() = REQUIRED_PERMISSIONS.all {
        ContextCompat.checkSelfPermission(baseContext, it) == PackageManager.PERMISSION_GRANTED
    }

    /** Запускает камеру после согласия пользователя или возвращает ошибку при отказе. */
    override fun onRequestPermissionsResult(
        requestCode: Int,
        permissions: Array<String>,
        grantResults: IntArray
    ) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults)
        if (requestCode == REQUEST_CODE_PERMISSIONS) {
            if (allPermissionsGranted()) startCamera()
            else finishWithResult("ERROR:Camera permission denied")
        }
    }

    /** Освобождает поток анализа при закрытии нативного экрана. */
    override fun onDestroy() {
        super.onDestroy()
        if (::cameraExecutor.isInitialized) cameraExecutor.shutdown()
    }

    /** Создаёт скруглённый фон для кнопки и подсказки сканера. */
    private fun roundedBackground(color: Int, radius: Float) = GradientDrawable().apply {
        setColor(color)
        cornerRadius = radius
        setStroke(dp(1), 0x884CE4FF.toInt())
    }

    /** Переводит значение dp в пиксели экрана текущего устройства. */
    private fun dp(value: Int): Int = (value * resources.displayMetrics.density).toInt()

    /** Затемняет область вокруг прозрачного окна и рисует рамку наведения на QR-код. */
    private class ScanFrameView(context: android.content.Context) : View(context) {
        // Кисти для затемнения фона, прозрачного окна и двух контуров рамки.
        private val dimPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = 0x99000000.toInt()
        }
        private val clearPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            xfermode = PorterDuffXfermode(PorterDuff.Mode.CLEAR)
        }
        private val glowPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = COLOR_CYAN
            style = Paint.Style.STROKE
            strokeWidth = dp(7).toFloat()
            strokeCap = Paint.Cap.ROUND
            strokeJoin = Paint.Join.ROUND
            setShadowLayer(dp(12).toFloat(), 0f, 0f, COLOR_CYAN)
        }
        private val linePaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = Color.WHITE
            style = Paint.Style.STROKE
            strokeWidth = dp(3).toFloat()
            strokeCap = Paint.Cap.ROUND
            strokeJoin = Paint.Join.ROUND
        }

        init {
            setLayerType(LAYER_TYPE_SOFTWARE, null)
            isClickable = false
        }

        override fun onDraw(canvas: Canvas) {
            super.onDraw(canvas)
            // Затемняем превью, оставляя внутри рамки прозрачный участок.
            canvas.drawRect(0f, 0f, width.toFloat(), height.toFloat(), dimPaint)

            // Размер окна зависит от обеих сторон экрана, поэтому рамка остаётся квадратной.
            val size = minOf(width * 0.76f, height * 0.34f)
            val centerY = height * 0.49f
            val frame = RectF(
                (width - size) / 2f,
                centerY - size / 2f,
                (width + size) / 2f,
                centerY + size / 2f
            )
            // Очищаем затемнение внутри рамки, чтобы камера оставалась видна.
            canvas.drawRoundRect(frame, dp(22).toFloat(), dp(22).toFloat(), clearPaint)

            // Светящийся контур и белые угловые метки помогают точно совместить QR-код с рамкой.
            val corner = size * 0.17f
            canvas.drawRoundRect(frame, dp(22).toFloat(), dp(22).toFloat(), glowPaint)
            canvas.drawLine(frame.left, frame.top + corner, frame.left, frame.top, linePaint)
            canvas.drawLine(frame.left, frame.top, frame.left + corner, frame.top, linePaint)
            canvas.drawLine(frame.right - corner, frame.top, frame.right, frame.top, linePaint)
            canvas.drawLine(frame.right, frame.top, frame.right, frame.top + corner, linePaint)
            canvas.drawLine(frame.left, frame.bottom - corner, frame.left, frame.bottom, linePaint)
            canvas.drawLine(frame.left, frame.bottom, frame.left + corner, frame.bottom, linePaint)
            canvas.drawLine(frame.right - corner, frame.bottom, frame.right, frame.bottom, linePaint)
            canvas.drawLine(frame.right, frame.bottom - corner, frame.right, frame.bottom, linePaint)
        }

        private fun dp(value: Int): Int = (value * resources.displayMetrics.density).toInt()
    }
}
