using Microsoft.Playwright;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

class Program
{
    // Nullable 경고 해결을 위해 기본값 및 ? 추가
    public class EtaRankingDb
    {
        public string CollectDate { get; set; } = string.Empty;
        public List<RankingItem> Rankings { get; set; } = new();
    }

    public class RankingItem
    {
        public int CharacterCode { get; set; }
        public int Rank { get; set; }
        public string? UserId { get; set; } // Null 허용
        public int Level { get; set; }
        public long Essence { get; set; }
    }

    public static async Task Main()
    {
        // 1. 환경 감지: GitHub Actions 인지 확인
        bool isGithubActions = Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";

        using var playwright = await Playwright.CreateAsync();
        // [중요] GitHub 서버에서는 반드시 Headless = true 여야 함
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = isGithubActions ? true : false
        });

        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36",
            ViewportSize = new ViewportSize { Width = 1920, Height = 1080 } // 화면 크기도 일반 모니터처럼 설정
        });
        var page = await context.NewPageAsync();

        var db = new EtaRankingDb { CollectDate = DateTime.Now.ToString("yyyy-MM-dd") };

        for (int cc = 0; cc <= 18; cc++)
        {
            Console.WriteLine($"캐릭터 코드 {cc} 수집 시작...");

            for (int p = 1; p <= 50; p++)
            {
                string url = $"https://tales.nexon.com/Community/Ranking/EtaRank?sc=7&cc={cc}&pagesize=100&page={p}";
                await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
                try
                {
                    await page.WaitForSelectorAsync("table tbody tr", new PageWaitForSelectorOptions { Timeout = 10000 });
                }
                catch
                {
                    Console.WriteLine($"[경고] 캐릭터 {cc}의 {p}페이지 로딩 실패 (타임아웃)");
                    break;
                }

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

                        // 안정적인 파싱을 위해 TryParse 사용 추천 (여기서는 단순화)
                        db.Rankings.Add(new RankingItem
                        {
                            CharacterCode = cc,
                            Rank = int.TryParse(rankText, out int r) ? r : 0,
                            UserId = ExtractId(nameText),
                            Level = int.TryParse(levelText, out int l) ? l : 0,
                            Essence = long.TryParse(essenceText, out long e) ? e : 0
                        });
                    }
                }

                Console.WriteLine($" - {p}페이지 수집 완료 (누적 {db.Rankings.Count}명)");

                if (rows.Count < 100) break; // 100개 미만이면 마지막 페이지
                await Task.Delay(500);
            }
        }

        // JSON 저장 로직
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        string jsonString = JsonSerializer.Serialize(db, jsonOptions);

        // [중요] 파일 경로를 현재 실행 경로의 상위(루트)로 지정하거나 직접 지정
        string fileName = "eta_ranking.json";
        await File.WriteAllTextAsync(fileName, jsonString, Encoding.UTF8);
        Console.WriteLine($"데이터 수집 완료: {fileName} 저장됨");

        // [변경] GitHub Actions 환경일 때는 여기서 Git 명령어를 실행하지 않음
        // (YAML 파일의 마지막 단계에서 Push를 처리하는 것이 더 안전함)
        if (!isGithubActions)
        {
            Console.WriteLine("로컬 환경이므로 수동 업로드를 실행하거나 종료합니다.");
            // 로컬인 경우에만 기존 UploadToGithub 실행 가능
        }
    }

    private static string ExtractId(string rawText)
    {
        if (string.IsNullOrEmpty(rawText)) return "Unknown";
        var match = Regex.Match(rawText, @"\(([^)]+)\)");
        if (match.Success) return match.Groups[1].Value;
        return rawText.Split(' ').Last().Trim();
    }
}