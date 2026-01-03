using System;
using System.Security.Claims;
using backendlog_in.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backendlog_in.Controllers;

[Authorize]
[ApiController]
[Route("api/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly AppDbContext _context;

    public DashboardController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet("user-info")]
    public async Task<IActionResult> GetUserInfo()
    {
        var idValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idValue, out var userId))
        {
            return Unauthorized();
        }

        var user = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new
            {
                u.Id,
                u.Name,
                u.Email,
                u.Phone,
                u.Gender,
                registrationDate = u.RegistrationDate.ToString("yyyy-MM-dd"),
                lastLogin = u.LastLogin.HasValue ? u.LastLogin.Value.ToString("yyyy-MM-dd HH:mm") : "Never"
            })
            .FirstOrDefaultAsync();

        return Ok(user);
    }

    [HttpGet("all-users")]
    public async Task<IActionResult> GetAllUsers()
    {
        var users = await _context.Users
            .AsNoTracking()
            .Where(u => u.IsActive)
            .OrderBy(u => u.Id)
            .Select(u => new
            {
                u.Id,
                u.Name,
                u.Email,
                u.Phone,
                u.Gender,
                registrationDate = u.RegistrationDate.ToString("yyyy-MM-dd")
            })
            .ToListAsync();

        return Ok(users);
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var totalUsers = await _context.Users.CountAsync();
        var activeUsers = await _context.Users.CountAsync(u => u.IsActive);

        return Ok(new
        {
            TotalUsers = totalUsers,
            ActiveUsers = activeUsers
        });
    }

    [HttpGet("courses")]
    public async Task<IActionResult> GetCourses()
    {
        var courses = await _context.Courses
            .AsNoTracking()
            .OrderBy(c => c.Id)
            .Select(c => new
            {
                c.Id,
                courseName = c.CourseName,
                description = c.Description,
                createdAt = c.CreatedAt.ToString("yyyy-MM-dd")
            })
            .ToListAsync();

        return Ok(courses);
    }
}