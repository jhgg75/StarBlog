namespace DataProc.Entities;

public class TranslationSettings {
    public const string SectionName = "Translation";

    /// <summary>
    /// 目标语言，如 "en"
    /// </summary>
    public string TargetLanguage { get; set; } = "en";

    /// <summary>
    /// 每次翻译请求之间的延迟（毫秒）
    /// </summary>
    public int DelayBetweenRequests { get; set; } = 2000;

    /// <summary>
    /// 最大重试次数
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// 内容最大长度（字符数），超过则分段翻译
    /// </summary>
    public int MaxContentLength { get; set; } = 6000;

    /// <summary>
    /// 超时秒数
    /// </summary>
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// 是否跳过已有翻译的文章
    /// </summary>
    public bool SkipExisting { get; set; } = true;
}
