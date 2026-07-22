namespace Configurator.Application.Services;

/// <summary>
/// Контакты, показываемые в диалоге помощи.
/// </summary>
public sealed class HelpOptions
{
    public const string SectionName = "Help";
    public const int MaximumContacts = 10;

    public List<HelpContactOptions> Contacts { get; set; } = [];
}

/// <summary>
/// Одна строка контактов в диалоге помощи.
/// </summary>
public sealed class HelpContactOptions
{
    public string Label { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Необязательная внешняя ссылка (http, https, mailto или tel).
    /// </summary>
    public string? Uri { get; set; }
}
