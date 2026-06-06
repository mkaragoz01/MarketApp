using System.ComponentModel.DataAnnotations;
using MarketApp.Models;

namespace MarketApp.DTOs;

public class CreateUserDto
{
    [Required(ErrorMessage = "Kullanıcı adı zorunludur.")]
    [RegularExpression(@".*\S.*", ErrorMessage = "Kullanıcı adı boş olamaz.")]
    [StringLength(100, ErrorMessage = "Kullanıcı adı en fazla 100 karakter olabilir.")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Şifre zorunludur.")]
    [MinLength(4, ErrorMessage = "Şifre en az 4 karakter olmalıdır.")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Rol zorunludur.")]
    [RegularExpression("^(Admin|User)$", ErrorMessage = "Rol Admin veya User olmalıdır.")]
    public string Role { get; set; } = UserRoles.User;
}
