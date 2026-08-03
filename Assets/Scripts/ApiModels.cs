// классы для JSON, которыми Unity общается с сервером.
using System;
using System.Collections.Generic;

[Serializable]
public class DepartmentItem
{
    public string name;
}

[Serializable]
public class DepartmentsResponse
{
    public List<DepartmentItem> departments = new List<DepartmentItem>();
}

[Serializable]
public class LoginRequest
{
    public string fullName;
    public string department;
    public string password;
}

[Serializable]
public class LoginResponse
{
    public bool success;
    public string message;
    public int userId;
    public string fullName;
    public string department;
    public int loginActionId;
}

[Serializable]
public class ScanRequest
{
    public int userId;
    public string qrCode;
}

[Serializable]
public class ScanResponse
{
    public bool success;
    public string message;
    public int scanId;
    public string objectName;
    public string result;
}

[Serializable]
public class ErrorResponse
{
    public bool success;
    public string message;
}