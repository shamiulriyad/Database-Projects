using System;

namespace backendlog_in.Models;

public class Course
{
    public int Id { get; set; }
    public string CourseName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}