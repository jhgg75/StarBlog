using System.Text;
using System.Text.RegularExpressions;
using DataProc.Entities;
using DataProc.Utilities;
using FluentResults;
using FreeSql;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StarBlog.Data.Models;

namespace DataProc.Services;

public class ArticleTranslator(
    ILogger<ArticleTranslator> logger,
    IBaseRepository<Post> postRepo,
    IBaseRepository<PostTranslation> translationRepo,
    LLM llm,
    IOptions<TranslationSettings> options
) : IService {
    private readonly TranslationSettings _settings = options.Value;

    public async Task<Result> Run() {
        var language = _settings.TargetLanguage;
        var total = await postRepo.Select.CountAsync();
        var posts = await postRepo.Where(p => p.IsPublish)
            .OrderByDescending(p => p.LastUpdateTime)
            .ToListAsync();

        logger.LogInformation("开始文章翻译 - 待处理: {Count}, 总数: {Total}, 目标语言: {Lang}",
            posts.Count, total, language);

        var successCount = 0;
        var failureCount = 0;
        var skipCount = 0;

        foreach (var post in posts) {
            try {
                // 检查是否已有翻译
                if (_settings.SkipExisting) {
                    var existing = await translationRepo
                        .Where(t => t.PostId == post.Id && t.Language == language)
                        .FirstAsync();
                    if (existing != null) {
                        logger.LogInformation("跳过已有翻译: {Title}", post.Title);
                        skipCount++;
                        continue;
                    }
                }

                if (string.IsNullOrWhiteSpace(post.Content)) {
                    logger.LogWarning("文章 [{Title}] 内容为空，跳过", post.Title);
                    skipCount++;
                    continue;
                }

                var result = await TranslatePostWithRetry(post, language);
                if (result.IsSuccess) {
                    successCount++;
                    logger.LogInformation("文章 [{Title}] 翻译完成", post.Title);
                }
                else {
                    failureCount++;
                    logger.LogError("文章 [{Title}] 翻译失败: {Error}", post.Title,
                        result.Errors.FirstOrDefault() .Message);
                }

                // 延迟以避免速率限制
                await Task.Delay(_settings.DelayBetweenRequests);
            }
            catch (Exception ex) {
                failureCount++;
                logger.LogError(ex, "处理文章 [{Title}] 时发生未预期错误", post.Title);
            }
        }

        logger.LogInformation("翻译完成 - 成功: {Success}, 失败: {Failure}, 跳过: {Skip}",
            successCount, failureCount, skipCount);
        return Result.Ok();
    }

    private async Task<Result> TranslatePostWithRetry(Post post, string language) {
        for (int attempt = 1; attempt <= _settings.MaxRetries; attempt++) {
            try {
                // 1. 翻译标题
                var title = await TranslateText(
                    PromptTemplates.ArticleTitleTranslation,
                    "title", post.Title, language);

                // 2. 翻译摘要
                string  summary = null;
                if (!string.IsNullOrWhiteSpace(post.Summary)) {
                    summary = await TranslateText(
                        PromptTemplates.ArticleSummaryTranslation,
                        "summary", post.Summary, language);
                }

                // 3. 翻译 Markdown 内容
                string  content = null;
                if (!string.IsNullOrWhiteSpace(post.Content)) {
                    content = await TranslateMarkdown(post.Content, language);
                }

                // 4. 保存翻译
                var translation = new PostTranslation {
                    Id = Guid.NewGuid().ToString(),
                    PostId = post.Id,
                    Language = language,
                    Title = title,
                    Summary = summary,
                    Content = content,
                    CreationTime = DateTime.Now,
                    LastUpdateTime = DateTime.Now
                };

                await translationRepo.InsertAsync(translation);
                return Result.Ok();
            }
            catch (Exception ex) {
                logger.LogWarning("文章 [{Title}] 第 {Attempt} 次尝试失败: {Error}",
                    post.Title, attempt, ex.Message);

                if (attempt == _settings.MaxRetries) {
                    return Result.Fail($"重试 {_settings.MaxRetries} 次后仍然失败: {ex.Message}");
                }

                // 指数退避
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                await Task.Delay(delay);
            }
        }

        return Result.Fail("未知错误");
    }

    private async Task<string> TranslateText(string template, string paramKey, string text, string language) {
        var prompt = PromptBuilder.Create(template)
            .AddParameter("language", language)
            .AddParameter(paramKey, text)
            .Build();

        return await GenerateStream(prompt);
    }

    private async Task<string> TranslateMarkdown(string markdown, string language) {
        var sections = SplitMarkdownBySections(markdown);
        var translatedSections = new List<string>();

        foreach (var section in sections) {
            if (section.Length > _settings.MaxContentLength) {
                // 大段落按段落拆分
                var paragraphs = section.Split("\n\n");
                var translatedParagraphs = new List<string>();
                foreach (var para in paragraphs) {
                    if (string.IsNullOrWhiteSpace(para)) {
                        translatedParagraphs.Add(para);
                        continue;
                    }

                    var translated = await TranslateSingleSection(para, language);
                    translatedParagraphs.Add(translated);
                }

                translatedSections.Add(string.Join("\n\n", translatedParagraphs));
            }
            else {
                translatedSections.Add(await TranslateSingleSection(section, language));
            }
        }

        return string.Join("\n", translatedSections);
    }

    private async Task<string> TranslateSingleSection(string section, string language) {
        var prompt = PromptBuilder.Create(PromptTemplates.ArticleContentTranslation)
            .AddParameter("language", language)
            .AddParameter("content", section)
            .Build();

        return await GenerateStream(prompt);
    }

    private async Task<string> GenerateStream(string prompt) {
        var sb = new StringBuilder();

        try {
            await foreach (var update in llm.GenerateTextStreamAsync(prompt)) {
                if (!string.IsNullOrEmpty(update.Text)) {
                    sb.Append(update.Text);
                    Console.Write(update.Text);
                }
            }

            Console.WriteLine();
            return sb.ToString().Trim();
        }
        catch (Exception ex) {
            logger.LogError(ex, "流式生成翻译时发生错误");
            throw;
        }
    }

    /// <summary>
    /// 按 Markdown 标题分段（H1/H2/H3）
    /// </summary>
    private static List<string> SplitMarkdownBySections(string markdown) {
        var lines = markdown.Split('\n');
        var sections = new List<string>();
        var current = new List<string>();

        foreach (var line in lines) {
            // 按 H1/H2/H3 分割
            if (Regex.IsMatch(line, @"^#{1,3}\s")) {
                if (current.Count > 0) {
                    sections.Add(string.Join("\n", current));
                    current.Clear();
                }
            }

            current.Add(line);
        }

        if (current.Count > 0) {
            sections.Add(string.Join("\n", current));
        }

        return sections;
    }
}
