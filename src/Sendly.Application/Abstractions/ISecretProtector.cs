namespace Sendly.Application.Abstractions;

public interface ISecretProtector
{
    string Protect(string plainText);

    string Unprotect(string protectedText);
}
