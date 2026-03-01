namespace Ralph.Core.Localization;

public interface IStringCatalog
{
    string Get(string key);
    string Format(string key, params object?[] args);
}
