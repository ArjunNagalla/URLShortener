namespace ShortUrl.Api.Services;

public interface ICodeGenerator
{
    string GenerateCode(int length = 6);
}
