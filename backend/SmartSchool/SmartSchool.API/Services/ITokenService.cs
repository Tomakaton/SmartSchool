using SmartSchool.API.Models;

namespace SmartSchool.API.Services;

public interface ITokenService
{
    AccessTokenResult CreateAccessToken(User user, IEnumerable<string> roles);

    string CreateRefreshToken();

    string HashRefreshToken(string refreshToken);
}
