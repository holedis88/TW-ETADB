using Microsoft.Playwright;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

class Program
{
    public class EtaRankingDb
    {
        public string CollectDate { get; set; }
        public List<RankingItem> Rankings { get; set; } = new();
    }

    public class RankingItem
    {
        public int CharacterCode { get; set; }
        public int Rank { get; set; }
        public string UserId { get; set; }
        public int Level { get; set; }
        public long Essence { get; set; }
    }

    public static async Task Main()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = false });
        var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();

        var db = new EtaRankingDb { CollectDate = DateTime.Now.ToString("yyyy-MM-dd") };

        for (int cc = 0; cc <= 18; cc++)
        {
            Console.WriteLine($"캐릭터 코드 {cc} 수집 시작...");

            for (int p = 1; p <= 50; p++)
            {
                string url = $"https://tales.nexon.com/Community/Ranking/EtaRank?sc=7&cc={cc}&pagesize=100&page={p}";
                await page.GotoAsync(url);

                try
                {
                    await page.WaitForSelectorAsync("table tbody tr", new PageWaitForSelectorOptions { Timeout = 3000 });
                }
                catch { break; }

                var rows = await page.QuerySelectorAllAsync("table tbody tr");

                if (rows.Count == 0 || (await rows[0].InnerTextAsync()).Contains("데이터가 없습니다")) break;

                foreach (var row in rows)
                {
                    var cols = await row.QuerySelectorAllAsync("td");
                    if (cols.Count >= 4)
                    {
                        string rankText = (await cols[0].InnerTextAsync()).Trim();
                        string nameText = (await cols[1].InnerTextAsync()).Trim();
                        string levelText = (await cols[2].InnerTextAsync()).Trim();
                        string essenceText = (await cols[3].InnerTextAsync()).Trim().Replace(",", "");

                        db.Rankings.Add(new RankingItem
                        {
                            CharacterCode = cc,
                            Rank = int.Parse(rankText),
                            UserId = ExtractId(nameText),
                            Level = int.Parse(levelText),
                            Essence = long.Parse(essenceText)
                        });
                    }
                }

                Console.WriteLine($" - {p}페이지 수집 완료 (누적 {db.Rankings.Count}명)");

                if (rows.Count < 100)
                {
                    Console.WriteLine($" - 캐릭터 {cc}: 마지막 페이지({p})이므로 다음 캐릭터로 이동합니다.");
                    break;
                }

                await Task.Delay(500);
            }
        }

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        string jsonString = JsonSerializer.Serialize(db, jsonOptions);

        string localFileName = "eta_ranking.json";
        await File.WriteAllTextAsync(localFileName, jsonString, Encoding.UTF8);
        Console.WriteLine($"임시 파일 저장 완료: {localFileName}");

        string rootPath = GetGitRoot(AppDomain.CurrentDomain.BaseDirectory);

        if (string.IsNullOrEmpty(rootPath))
        {
            Console.WriteLine("오류: .git 폴더를 찾을 수 없습니다. Git 설정을 확인하세요.");
            return;
        }

        string targetPath = Path.Combine(rootPath, "eta_ranking.json");
        File.Copy(localFileName, targetPath, true);
        Console.WriteLine($"루트 경로로 복사 완료: {targetPath}");

        await Task.Delay(500);
        UploadToGithub(rootPath);

    }

    static string GetGitRoot(string startPath)
    {
        var directory = new DirectoryInfo(startPath);
        while (directory != null)
        {
            if (directory.GetDirectories(".git").Any())
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        return null;
    }


    static void UploadToGithub(string rootPath)
    {
        try
        {
            Console.WriteLine($"GitHub 업로드 시작 (작업 디렉토리: {rootPath})");

            RunCommand("git add *.json", rootPath);
            RunCommand($"git commit -m \"Update: {DateTime.Now:yyyy-MM-dd}\"", rootPath);
            RunCommand("git push origin main", rootPath);

            Console.WriteLine("GitHub 업데이트 성공!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"업로드 실패: {ex.Message}");
        }
    }

    static void RunCommand(string command, string workingDirectory)
    {
        var processInfo = new ProcessStartInfo("cmd.exe", "/c " + command)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        using var process = Process.Start(processInfo);
        process?.WaitForExit();

        string error = process?.StandardError.ReadToEnd();
        if (!string.IsNullOrEmpty(error)) Console.WriteLine($"Git 에러 메시지: {error}");
    }

    private static string ExtractId(string rawText)
    {
        var match = Regex.Match(rawText, @"\(([^)]+)\)");
        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        return rawText.Split(' ').Last().Trim();
    }
}