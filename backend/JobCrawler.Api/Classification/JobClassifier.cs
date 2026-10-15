using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using JobCrawler.Api.Models;

namespace JobCrawler.Api.Classification;

public record Classification(bool IsIt, string Category, string Level, int Score);

/// <summary>
/// Rule-based classifier: weighted keyword matching over title, site category, skills and description snippet.
///
/// Weights: a ROLE word in the title (tester, business analyst, devops...) = 6, a TECH word in the title (python, react...) = 3,
/// site category / skills = 2, description snippet = 1. Roles outweigh techs because titles often list the stack in brackets:
/// "QA Automation Engineer (Python, JS)" is QA, not Backend.
///
/// Matching rules (each one fixes a real false positive seen in the crawled data):
///  - Text is lower-cased and Vietnamese diacritics are removed ("lập trình nhúng" -> "lap trinh nhung").
///  - Keywords match on word boundaries, so "java" does not hit "javascript".
///  - Longest keywords are matched first and their span is blanked out, so "react native" counts for Mobile only,
///    not also for Frontend via "react".
///  - Short acronyms (IT, AI, BA, QA, PM, SAP...) are matched case-sensitively on the ORIGINAL text, because once
///    lower-cased and stripped of diacritics they collide with Vietnamese words: "ít" -> "it", "ai" ("who") -> "ai",
///    "sắp" ("soon") -> "sap". For the same reason "nhưng" ("but") -> "nhung" is never used alone for Embedded,
///    and "hạ tầng" is not an IT word (construction jobs use it too).
///  - IT job boards also list sales / marketing / graphic-design / CNC jobs in their IT category, so a title with a
///    non-IT role word and no IT role word is Non-IT whatever the source. Careful with prefixes: "Tuyển dụng ..." means
///    "hiring ...", not an HR job, and "kiến trúc sư hệ thống" is a system architect, not a building architect.
/// </summary>
public static partial class JobClassifier
{
    public const string NonIt = "Non-IT";
    public const string OtherIt = "Other IT";

    /// <summary>Sources whose crawled listing is IT-only, so a job is IT even without a keyword hit (unless its title is a non-IT role).</summary>
    private static readonly HashSet<string> ItOnlySources = ["itviec", "vietnamworks"];

    private record Keyword(string Category, string Text, bool IsRole, bool Acronym, Regex Regex);

    private record Rule(string Category, string[] Roles, string[] Techs, string[] RoleAcronyms, string[]? TechAcronyms = null);

    // Order matters only for ties: earlier categories win.
    private static readonly Rule[] Rules =
    [
        new("Fullstack", ["fullstack", "full-stack", "full stack"], [], []),
        new("Mobile", ["mobile developer", "mobile engineer", "lap trinh mobile", "android developer", "ios developer",
                "flutter developer", "mobile"],
            ["android", "ios", "flutter", "react native", "swift", "kotlin", "xamarin", "dart"], []),
        new("Frontend", ["frontend", "front-end", "front end", "web developer", "ui developer"],
            ["reactjs", "react", "react.js", "vue", "vuejs", "vue.js", "angular", "javascript", "typescript", "html", "html5",
                "css", "next.js", "nextjs", "nuxt", "jquery"], []),
        new("Backend", ["backend", "back-end", "back end", "software architect", "kien truc su phan mem", "kien truc phan mem",
                "kien truc su he thong", "system architect"],
            ["java", "spring", "spring boot", "j2ee", ".net", ".net core", "c#", "asp.net", "golang", "php", "laravel", "node.js",
                "nodejs", "nestjs", "python", "django", "fastapi", "flask", "ruby", "ruby on rails", "microservices", "restful",
                "api", "c++", "rust", "scala", "elixir", "pl/sql"], []),
        new("DevOps/Cloud", ["devops", "devsecops", "site reliability", "cloud engineer", "platform engineer", "mlops",
                "system engineer", "ky su he thong"],
            ["cloud", "aws", "azure", "gcp", "google cloud", "kubernetes", "k8s", "docker", "terraform", "ci/cd", "jenkins",
                "ansible"], ["SRE"]),
        new("Data/AI", ["data engineer", "data analyst", "data scientist", "data science", "machine learning", "ai engineer",
                "deep learning", "data labeling", "gan nhan du lieu", "phan tich du lieu", "tri tue nhan tao", "data governance",
                "data quality", "data warehouse", "data analytics", "bi developer", "chuyen vien du lieu", "chuyen gia du lieu",
                "ky su du lieu"],
            ["llm", "nlp", "computer vision", "big data", "spark", "etl", "power bi", "tableau", "databricks",
                "artificial intelligence", "generative ai", "genai", "agentic ai", "pandas", "pytorch", "tensorflow",
                "hugging face"], ["AI", "ML", "BI"]),
        new("QA/Tester", ["tester", "testing", "test engineer", "automation test", "manual test", "kiem thu", "quality assurance",
                "quality engineer", "qa engineer", "qc engineer", "kiem soat chat luong phan mem"],
            ["selenium", "cypress", "playwright", "appium", "jmeter", "postman"], ["QA", "QC"]),
        new("Security", ["security", "bao mat", "an ninh mang", "an toan thong tin", "pentest", "penetration", "cyber"],
            ["grc", "iso 27001", "siem", "owasp"], ["SOC"]),
        new("Embedded/IoT", ["embedded", "lap trinh nhung", "phan mem nhung", "he thong nhung", "ky su nhung", "firmware",
                "iot", "fpga", "vi mach", "ic design"],
            ["autosar", "rtos", "microcontroller", "vxworks"], []),
        new("Game", ["game developer", "game designer", "lap trinh game"], ["game", "unity", "unreal", "cocos"], []),
        new("BA/PM/PO", ["business analyst", "business analysis", "system analyst", "phan tich nghiep vu", "phan tich he thong",
                "product owner", "product manager", "project manager", "scrum master", "brse", "bridge engineer", "it comtor",
                "comtor", "delivery manager", "quan ly du an"],
            ["jira", "agile", "scrum"], ["BA", "PM", "PO"]),
        new("UI/UX Design", ["ui/ux", "ux/ui", "ux designer", "ui designer", "web designer", "product designer", "thiet ke giao dien"],
            ["figma"], ["UX", "UI"]),
        new("IT Support/Infra", ["helpdesk", "help desk", "it support", "ho tro ky thuat", "system admin", "system administrator",
                "sysadmin", "network engineer", "quan tri mang", "quan tri he thong", "dba", "database administrator",
                "it infrastructure", "data center", "ky thuat it", "it operations", "van hanh co so du lieu", "quan tri co so du lieu"],
            ["network", "linux", "windows server", "vien thong", "ccna", "vmware"], []),
        new("ERP/Consulting", ["erp", "odoo", "oracle ebs", "dynamics 365", "salesforce", "it consultant", "presales", "pre-sales",
                "solution architect", "kien truc su giai phap", "sap consultant", "trien khai phan mem"],
            ["sap abap", "sap fico"], ["SAP"]),
    ];

    /// <summary>Generic IT words: they make a job IT but say nothing about its category.</summary>
    private static readonly string[] GenericIt =
    [
        "engineer", "lap trinh", "phan mem", "software", "cong nghe thong tin", "tech lead", "sql", "database", "git",
    ];

    /// <summary>Generic words that are enough on their own, in a title, to make a job IT ("ServiceNow ITSM Developer").
    /// "engineer" is not one of them: process / civil / sales engineers are common on mixed boards.</summary>
    private static readonly string[] GenericItRoles =
    [
        "developer", "lap trinh vien", "software engineer", "software developer", "programmer", "coder",
    ];

    private static readonly string[] GenericItAcronyms = ["IT", "CNTT"];

    /// <summary>Title words of roles that are not IT work even when posted in an IT category.</summary>
    [GeneratedRegex(@"(?<![\p{L}\p{N}])(sales|sale|kinh doanh|ban hang|marketing|ke toan|accountant|accounting|nhan su|" +
                    @"nhan vien tuyen dung|chuyen vien tuyen dung|recruiter|talent acquisition|hr|cham soc khach hang|telesales|b2b|b2c|" +
                    @"graphic design|thiet ke do hoa|hoa vien|thiet ke kien truc|noi that|xay dung|brand|content creator|" +
                    @"video editor|dung phim|cnc|co khi)(?![\p{L}\p{N}])")]
    private static partial Regex NonItTitleRegex();

    private static readonly List<Keyword> Keywords = BuildKeywords();

    public static Classification Classify(JobPosting j)
    {
        var fields = new (string? Text, bool IsTitle, int Weight)[]
        {
            (j.Title, true, 3),
            (j.SourceCategory, false, 2),
            (j.Skills, false, 2),
            (j.Snippet, false, 1),
        };

        var scores = new Dictionary<string, int>();
        var generic = 0;
        var roleInTitle = false;

        foreach (var (text, isTitle, baseWeight) in fields)
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            var normalized = Normalize(text);
            var original = text;

            foreach (var k in Keywords)
            {
                // Acronyms are only trusted in short structured fields; descriptions are full of stray capitals.
                if (k.Acronym && baseWeight == 1) continue;

                var target = k.Acronym ? original : normalized;
                var m = k.Regex.Match(target);
                if (!m.Success) continue;

                // Blank out the match so shorter keywords inside it do not score again.
                var blank = new string(' ', m.Length);
                if (k.Acronym) original = original.Remove(m.Index, m.Length).Insert(m.Index, blank);
                else normalized = normalized.Remove(m.Index, m.Length).Insert(m.Index, blank);

                var weight = isTitle && k.IsRole ? 6 : baseWeight;
                if (isTitle && k.IsRole && k.Category != "") roleInTitle = true;

                if (k.Category == "") generic += weight;
                else scores[k.Category] = scores.GetValueOrDefault(k.Category) + weight;
            }
        }

        var best = Rules.Select((r, i) => (r.Category, Score: scores.GetValueOrDefault(r.Category), Order: i))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score).ThenBy(x => x.Order)
            .FirstOrDefault();

        // A title saying "fullstack" wins outright; otherwise strong frontend + backend signals together also mean fullstack.
        var category = best.Category;
        if (scores.GetValueOrDefault("Fullstack") >= 6 ||
            (scores.GetValueOrDefault("Frontend") >= 4 && scores.GetValueOrDefault("Backend") >= 4 && category is "Frontend" or "Backend"))
            category = "Fullstack";

        var nonItTitle = NonItTitleRegex().IsMatch(Normalize(j.Title)) && !roleInTitle;
        var itScore = scores.Values.Sum() + generic;
        var isIt = !nonItTitle && (ItOnlySources.Contains(j.Source) || itScore >= 4);

        category = !isIt ? NonIt : category ?? OtherIt;
        return new Classification(isIt, category, DetectLevel(j), best.Score);
    }

    public static string DetectLevel(JobPosting j)
    {
        // "Product/Project Manager" is a role, not a seniority level.
        var title = RoleManagerRegex().Replace(Normalize(j.Title), " ");
        var level = FirstLevelIn(title);
        if (level != null) return level;

        var sourceLevel = Normalize(j.SourceLevel ?? "");
        level = sourceLevel switch
        {
            var s when s.Contains("thuc tap") || s.Contains("sinh vien") => "Intern",
            var s when s.Contains("moi tot nghiep") || s.Contains("duoi 1 nam") || s.Contains("khong yeu cau kinh nghiem") => "Fresher",
            var s when s.Contains("giam doc") || s.Contains("truong phong") || s.Contains("quan ly") => "Manager",
            var s when s.Contains("truong nhom") || s.Contains("giam sat") => "Lead",
            _ => null,
        };
        if (level != null) return level;

        var years = YearsRegex().Match(sourceLevel + " " + Normalize(j.Snippet ?? ""));
        if (years.Success && double.TryParse(years.Groups[1].Value.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var y))
            return y switch { < 1 => "Fresher", < 3 => "Junior", < 5 => "Middle", _ => "Senior" };

        return "Unspecified";
    }

    private static readonly (string Level, Regex Regex)[] LevelRules =
    [
        ("Intern", Word("intern|internship|thuc tap|thuc tap sinh|trainee")),
        ("Fresher", Word("fresher|entry level|moi tot nghiep|graduate")),
        ("Junior", Word("junior|jr")),
        ("Middle", Word("middle|mid-level|mid level|mid")),
        ("Senior", Word("senior|sr|cao cap|expert|chuyen gia")),
        ("Lead", Word("lead|leader|team lead|tech lead|teamleader|truong nhom|principal|architect|staff engineer")),
        ("Manager", Word("manager|head of|director|truong phong|giam doc|pho phong|cto|cio|quan ly")),
    ];

    /// <summary>Earliest level word in the title wins ("Junior/Middle" -> Junior).</summary>
    private static string? FirstLevelIn(string text) =>
        LevelRules.Select(r => (r.Level, Match: r.Regex.Match(text)))
            .Where(x => x.Match.Success)
            .OrderBy(x => x.Match.Index)
            .Select(x => x.Level)
            .FirstOrDefault();

    public static string Normalize(string s)
    {
        var d = s.ToLowerInvariant().Replace('đ', 'd').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static List<Keyword> BuildKeywords()
    {
        var list = new List<Keyword>();
        foreach (var r in Rules)
        {
            list.AddRange(r.Roles.Select(w => new Keyword(r.Category, w, true, false, Word(Regex.Escape(w)))));
            list.AddRange(r.Techs.Select(w => new Keyword(r.Category, w, false, false, Word(Regex.Escape(w)))));
            list.AddRange(r.RoleAcronyms.Select(a => new Keyword(r.Category, a, true, true, Word(Regex.Escape(a)))));
            list.AddRange((r.TechAcronyms ?? []).Select(a => new Keyword(r.Category, a, false, true, Word(Regex.Escape(a)))));
        }
        list.AddRange(GenericIt.Select(w => new Keyword("", w, false, false, Word(Regex.Escape(w)))));
        list.AddRange(GenericItRoles.Select(w => new Keyword("", w, true, false, Word(Regex.Escape(w)))));
        list.AddRange(GenericItAcronyms.Select(a => new Keyword("", a, false, true, Word(Regex.Escape(a)))));
        // Longest first so multi-word phrases consume their parts.
        return list.OrderByDescending(k => k.Text.Length).ToList();
    }

    private static Regex Word(string pattern) =>
        new($@"(?<![\p{{L}}\p{{N}}])(?:{pattern})(?![\p{{L}}\p{{N}}])", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [GeneratedRegex(@"(product|project|delivery|account|brand)\s+manager|quan ly du an|quan ly san pham")]
    private static partial Regex RoleManagerRegex();

    [GeneratedRegex(@"(\d+(?:[.,]\d+)?)\s*\+?\s*(?:nam|years?|yoe|yrs)")]
    private static partial Regex YearsRegex();
}
