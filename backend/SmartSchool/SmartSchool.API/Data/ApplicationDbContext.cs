using Microsoft.EntityFrameworkCore;

namespace SmartSchool.API.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options);