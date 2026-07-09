namespace PharmaPOS.Application.Features.Authentication;

public record LoginRequest(string Username, string Password, bool RememberMe = false);

public record ChangePasswordRequest(int UserId, string CurrentPassword, string NewPassword);
