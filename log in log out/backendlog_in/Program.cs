using Microsoft.AspNetCore.Authentication.JwtBearer;
using backendlog_in.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Data.Common;

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");
}

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));


var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey))
{
    throw new InvalidOperationException("Jwt:Key is not configured.");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });


builder.Services.AddCors(options =>
{
    options.AddPolicy("ReactAppPolicy",
        builder => builder
            .WithOrigins("http://localhost:3000" )
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials());
});

var app = builder.Build();


if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("ReactAppPolicy");
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

static string QuoteIdentifier(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

static async Task<string?> GetActualTableNameAsync(DbConnection connection, string tableNameLower)
{
    await using var cmd = connection.CreateCommand();
    cmd.CommandText = @"
SELECT table_name
FROM information_schema.tables
WHERE table_schema = 'public' AND lower(table_name) = lower(@t)
LIMIT 1;";

    var p = cmd.CreateParameter();
    p.ParameterName = "t";
    p.Value = tableNameLower;
    cmd.Parameters.Add(p);

    var result = await cmd.ExecuteScalarAsync();
    return result as string;
}

static async Task<string?> GetExactTableNameAsync(DbConnection connection, string exactTableName)
{
    await using var cmd = connection.CreateCommand();
    cmd.CommandText = @"
SELECT table_name
FROM information_schema.tables
WHERE table_schema = 'public' AND table_name = @t
LIMIT 1;";

    var p = cmd.CreateParameter();
    p.ParameterName = "t";
    p.Value = exactTableName;
    cmd.Parameters.Add(p);

    var result = await cmd.ExecuteScalarAsync();
    return result as string;
}

static async Task<string?> GetActualColumnNameAsync(DbConnection connection, string actualTableName, string columnNameLower)
{
    await using var cmd = connection.CreateCommand();
    cmd.CommandText = @"
SELECT column_name
FROM information_schema.columns
WHERE table_schema = 'public'
  AND table_name = @t
  AND lower(column_name) = lower(@c)
LIMIT 1;";

    var p1 = cmd.CreateParameter();
    p1.ParameterName = "t";
    p1.Value = actualTableName;
    cmd.Parameters.Add(p1);

    var p2 = cmd.CreateParameter();
    p2.ParameterName = "c";
    p2.Value = columnNameLower;
    cmd.Parameters.Add(p2);

    var result = await cmd.ExecuteScalarAsync();
    return result as string;
}

static async Task<string?> GetExactColumnNameAsync(DbConnection connection, string actualTableName, string exactColumnName)
{
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT column_name
FROM information_schema.columns
WHERE table_schema = 'public'
    AND table_name = @t
    AND column_name = @c
LIMIT 1;";

        var p1 = cmd.CreateParameter();
        p1.ParameterName = "t";
        p1.Value = actualTableName;
        cmd.Parameters.Add(p1);

        var p2 = cmd.CreateParameter();
        p2.ParameterName = "c";
        p2.Value = exactColumnName;
        cmd.Parameters.Add(p2);

        var result = await cmd.ExecuteScalarAsync();
        return result as string;
}

static async Task EnsureDatabaseSchemaAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbSchema");
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    await dbContext.Database.EnsureCreatedAsync();

    try
    {
        await using var connection = dbContext.Database.GetDbConnection();
        await connection.OpenAsync();

        // Normalize table name casing to match EF Core default ("Users")
        var usersTable = await GetExactTableNameAsync(connection, "Users");
        if (string.IsNullOrWhiteSpace(usersTable))
        {
            var existingUsersTable = await GetActualTableNameAsync(connection, "users");
            if (!string.IsNullOrWhiteSpace(existingUsersTable) && existingUsersTable != "Users")
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    $"ALTER TABLE {QuoteIdentifier(existingUsersTable)} RENAME TO {QuoteIdentifier("Users")};");
                usersTable = "Users";
            }
        }

        if (string.IsNullOrWhiteSpace(usersTable))
        {
            // Fresh DB with no tables yet (or different schema); nothing to normalize.
            return;
        }

        async Task EnsureColumnAsync(string expected, string? renameFromLower = null, string? addSqlTypeAndDefault = null)
        {
            var expectedCol = await GetExactColumnNameAsync(connection, usersTable, expected);
            if (!string.IsNullOrWhiteSpace(expectedCol)) return;

            if (!string.IsNullOrWhiteSpace(renameFromLower))
            {
                var fromCol = await GetActualColumnNameAsync(connection, usersTable, renameFromLower);
                if (!string.IsNullOrWhiteSpace(fromCol))
                {
                    await dbContext.Database.ExecuteSqlRawAsync(
                        $"ALTER TABLE {QuoteIdentifier(usersTable)} RENAME COLUMN {QuoteIdentifier(fromCol)} TO {QuoteIdentifier(expected)};");
                    return;
                }
            }

            if (!string.IsNullOrWhiteSpace(addSqlTypeAndDefault))
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    $"ALTER TABLE {QuoteIdentifier(usersTable)} ADD COLUMN {QuoteIdentifier(expected)} {addSqlTypeAndDefault};");
            }
        }

       
        await EnsureColumnAsync("Id", renameFromLower: "id");
        await EnsureColumnAsync("Name", renameFromLower: "name");
        await EnsureColumnAsync("Email", renameFromLower: "email");
        await EnsureColumnAsync("Phone", renameFromLower: "phone");
        await EnsureColumnAsync("Gender", renameFromLower: "gender");

        
        var passwordHashCol = await GetExactColumnNameAsync(connection, usersTable, "PasswordHash");
        if (string.IsNullOrWhiteSpace(passwordHashCol))
        {
            var oldPasswordCol = await GetActualColumnNameAsync(connection, usersTable, "password");
            if (!string.IsNullOrWhiteSpace(oldPasswordCol))
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    $"ALTER TABLE {QuoteIdentifier(usersTable)} RENAME COLUMN {QuoteIdentifier(oldPasswordCol)} TO {QuoteIdentifier("PasswordHash")};");
            }
            else
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    $"ALTER TABLE {QuoteIdentifier(usersTable)} ADD COLUMN {QuoteIdentifier("PasswordHash")} text NOT NULL DEFAULT '';");
            }
        }

       
        if (string.IsNullOrWhiteSpace(await GetExactColumnNameAsync(connection, usersTable, "RegistrationDate")))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                $"ALTER TABLE {QuoteIdentifier(usersTable)} ADD COLUMN {QuoteIdentifier("RegistrationDate")} timestamp with time zone NOT NULL DEFAULT (now() at time zone 'utc');");
        }

        if (string.IsNullOrWhiteSpace(await GetExactColumnNameAsync(connection, usersTable, "LastLogin")))
        {
            await dbContext.Database.ExecuteSqlRawAsync($"ALTER TABLE {QuoteIdentifier(usersTable)} ADD COLUMN {QuoteIdentifier("LastLogin")} timestamp with time zone NULL;");
        }

        if (string.IsNullOrWhiteSpace(await GetExactColumnNameAsync(connection, usersTable, "IsActive")))
        {
            await dbContext.Database.ExecuteSqlRawAsync($"ALTER TABLE {QuoteIdentifier(usersTable)} ADD COLUMN {QuoteIdentifier("IsActive")} boolean NOT NULL DEFAULT true;");
        }

        // Ensure Courses table exists with expected casing/columns.
        var coursesTable = await GetExactTableNameAsync(connection, "Courses");
        if (string.IsNullOrWhiteSpace(coursesTable))
        {
            var existingCoursesTable = await GetActualTableNameAsync(connection, "courses");
            if (!string.IsNullOrWhiteSpace(existingCoursesTable) && existingCoursesTable != "Courses")
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    $"ALTER TABLE {QuoteIdentifier(existingCoursesTable)} RENAME TO {QuoteIdentifier("Courses")};");
                coursesTable = "Courses";
            }
        }

        if (string.IsNullOrWhiteSpace(coursesTable))
        {
            await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE ""Courses"" (
                ""Id"" serial PRIMARY KEY,
                ""CourseName"" text NOT NULL,
                ""Description"" text NOT NULL,
                ""CreatedAt"" timestamp with time zone NOT NULL DEFAULT (now() at time zone 'utc')
            );");
            coursesTable = "Courses";
        }

        async Task EnsureCourseColumnAsync(string expected, string? renameFromLower = null, string? addSql = null)
        {
            var exact = await GetExactColumnNameAsync(connection, coursesTable, expected);
            if (!string.IsNullOrWhiteSpace(exact)) return;

            if (!string.IsNullOrWhiteSpace(renameFromLower))
            {
                var from = await GetActualColumnNameAsync(connection, coursesTable, renameFromLower);
                if (!string.IsNullOrWhiteSpace(from))
                {
                    await dbContext.Database.ExecuteSqlRawAsync(
                        $"ALTER TABLE {QuoteIdentifier(coursesTable)} RENAME COLUMN {QuoteIdentifier(from)} TO {QuoteIdentifier(expected)};");
                    return;
                }
            }

            if (!string.IsNullOrWhiteSpace(addSql))
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    $"ALTER TABLE {QuoteIdentifier(coursesTable)} ADD COLUMN {QuoteIdentifier(expected)} {addSql};");
            }
        }

        await EnsureCourseColumnAsync("Id", renameFromLower: "id", addSql: "serial PRIMARY KEY");
        await EnsureCourseColumnAsync("CourseName", renameFromLower: "coursename", addSql: "text NOT NULL");
        await EnsureCourseColumnAsync("Description", renameFromLower: "description", addSql: "text NOT NULL");
        await EnsureCourseColumnAsync("CreatedAt", renameFromLower: "createdat", addSql: "timestamp with time zone NOT NULL DEFAULT (now() at time zone 'utc')");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Database schema check/repair failed.");
       
    }
}

await EnsureDatabaseSchemaAsync(app.Services);

app.Run();