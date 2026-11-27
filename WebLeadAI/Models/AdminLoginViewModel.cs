using System.ComponentModel.DataAnnotations;

namespace WebLeadAI.Models;

public sealed class AdminLoginViewModel
{
    [Required(ErrorMessage = "Ievadi lietotājvārdu.")]
    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Ievadi paroli.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public string? ReturnUrl { get; set; }
}
