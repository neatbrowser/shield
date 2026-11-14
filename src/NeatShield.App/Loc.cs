using System.Globalization;
using System.Text.RegularExpressions;

namespace NeatShield.App;

/// <summary>All texts of the window, in Vietnamese and English. <c>T("key")</c> gives the one for the language in use.</summary>
public static class Loc
{
    // key -> (vi, en)
    private static readonly Dictionary<string, (string vi, string en)> Texts = new()
    {
        ["on"] = ("Bật", "On"),
        ["off"] = ("Tắt", "Off"),

        ["nav.home"] = ("Trang chủ", "Home"),
        ["nav.filters"] = ("Bộ lọc", "Filters"),
        ["nav.custom"] = ("Bộ lọc của tôi", "My filters"),
        ["nav.stats"] = ("Thống kê", "Statistics"),
        ["nav.settings"] = ("Cài đặt", "Settings"),

        ["home.on"] = ("Bảo vệ đang bật", "Protection is on"),
        ["home.on.sub"] = ("Chặn quảng cáo và theo dõi cho trình duyệt và ứng dụng của bạn. Bộ lọc: {0}.", "Blocking ads and trackers for your browser and apps. Filters: {0}."),
        ["home.starting.title"] = ("Đang bật bảo vệ…", "Turning protection on…"),
        ["home.error"] = ("Không bật được bảo vệ", "Could not turn protection on"),
        ["home.off"] = ("Bảo vệ đang tắt", "Protection is off"),
        ["home.off.sub"] = ("Bật để chặn quảng cáo và theo dõi.", "Turn it on to block ads and trackers."),
        ["home.nofilters.value"] = ("chưa có", "none yet"),
        ["home.cert.title"] = ("Cần chứng chỉ NeatShield để lọc trang HTTPS", "The NeatShield certificate is needed to filter HTTPS pages"),
        ["home.cert.msg"] = ("Windows sẽ hỏi bạn xác nhận khi cài chứng chỉ. Chứng chỉ được tạo trên máy này và khóa riêng không rời khỏi máy.", "Windows will ask you to confirm the installation. The certificate is created on this computer and its private key never leaves it."),
        ["home.cert.button"] = ("Cài chứng chỉ", "Install certificate"),
        ["home.nofilters.title"] = ("Chưa có bộ lọc", "No filters yet"),
        ["home.nofilters.msg"] = ("Mở trang Bộ lọc và bấm Cập nhật ngay.", "Open the Filters page and press Update now."),
        ["home.blocked.run"] = ("Đã chặn (lần chạy này)", "Blocked (this run)"),
        ["home.blocked.total"] = ("Tổng số đã chặn", "Blocked in total"),
        ["home.pages"] = ("Trang đã dọn sạch", "Pages cleaned"),
        ["home.requests"] = ("Yêu cầu đã xử lý", "Requests handled"),
        ["home.test"] = ("Kiểm tra bảo vệ", "Test protection"),
        ["home.test.hint"] = ("Kiểm tra xem bộ lọc, proxy, chứng chỉ HTTPS và cài đặt Windows có hoạt động không. Không gửi gì ra Internet.", "Checks that the filters, the proxy, the HTTPS certificate and the Windows setting all work. Nothing is sent to the Internet."),
        ["home.test.off"] = ("Hãy bật bảo vệ trước rồi kiểm tra lại.", "Turn protection on first, then test again."),
        ["home.test.pass"] = ("Mọi thứ hoạt động tốt.", "Everything works."),
        ["home.test.fail"] = ("Có vấn đề, xem các dòng bên dưới. Bạn có thể sao chép báo cáo ở Cài đặt → Khắc phục sự cố.", "Something is wrong, see the lines below. You can copy a report in Settings → Troubleshooting."),
        ["selftest.filters"] = ("Đã nạp bộ lọc", "Filters loaded"),
        ["selftest.proxy"] = ("Proxy đang chạy", "Proxy running"),
        ["selftest.https"] = ("Kết nối HTTPS qua NeatShield", "HTTPS through NeatShield"),
        ["selftest.rules"] = ("Luật chặn quảng cáo", "Ad-blocking rules"),
        ["selftest.system"] = ("Windows dùng NeatShield", "Windows uses NeatShield"),

        ["filters.title"] = ("Bộ lọc", "Filters"),
        ["filters.version"] = ("Phiên bản: {0}", "Version: {0}"),
        ["filters.none"] = ("Chưa có bộ lọc.", "No filters yet."),
        ["filters.rules"] = ("{0} quy tắc mạng · {1} quy tắc ẩn phần tử", "{0} network rules · {1} element-hiding rules"),
        ["filters.lastcheck"] = ("Kiểm tra lần cuối: {0}", "Last checked: {0}"),
        ["filters.nocheck"] = ("Chưa kiểm tra trong lần chạy này.", "Not checked yet in this run."),
        ["filters.update"] = ("Cập nhật ngay", "Update now"),
        ["filters.updated"] = ("Đã cập nhật", "Updated"),
        ["filters.updated.msg"] = ("Phiên bản {0}", "Version {0}"),
        ["filters.uptodate"] = ("Đã là bản mới nhất", "Already up to date"),
        ["filters.failed"] = ("Cập nhật thất bại", "Update failed"),
        ["filters.note"] = ("Bộ lọc được tải từ bản phát hành mới nhất của neatbrowser/neat-filters trên GitHub. Chỉ các tệp đã thay đổi mới được tải lại, và mỗi tệp được kiểm tra kích thước và SHA-256 trước khi dùng.", "Filters come from the latest release of neatbrowser/neat-filters on GitHub. Only changed files are downloaded again, and every file's size and SHA-256 are checked before use."),
        ["progress.manifest"] = ("Đang tải manifest.json...", "Downloading manifest.json..."),
        ["progress.file"] = ("Đang tải {0} ({1}/{2})...", "Downloading {0} ({1}/{2})..."),

        ["stats.title"] = ("Thống kê", "Statistics"),
        ["stats.summary"] = ("Từ {0}: {1} yêu cầu đã bị chặn. Lần chạy này: {2} yêu cầu, {3} bị chặn, {4} được thay thế an toàn, {5} bỏ tham số theo dõi, {6} trang đã ẩn quảng cáo, {7} popup bị chặn.", "Since {0}: {1} requests blocked. This run: {2} requests, {3} blocked, {4} replaced safely, {5} tracking parameters removed, {6} pages cleaned, {7} pop-ups blocked."),
        ["stats.top"] = ("Tên miền bị chặn nhiều nhất", "Most blocked hosts"),
        ["stats.recent"] = ("Gần đây", "Recent"),

        ["custom.title"] = ("Bộ lọc của tôi", "My filters"),
        ["custom.intro"] = ("Viết quy tắc của riêng bạn, mỗi dòng một quy tắc (cú pháp Adblock Plus / uBlock Origin). Dòng bắt đầu bằng ! là ghi chú. Dòng không hợp lệ bị bỏ qua.", "Write your own rules, one per line (Adblock Plus / uBlock Origin syntax). Lines starting with ! are comments. Invalid lines are ignored."),
        ["custom.examples"] = ("Ví dụ:", "Examples:"),
        ["custom.example1"] = ("||ads.example.com^                      chặn một tên miền", "||ads.example.com^                      block a host"),
        ["custom.example2"] = ("example.com##.banner                    ẩn phần tử trên example.com", "example.com##.banner                    hide an element on example.com"),
        ["custom.example3"] = ("example.com##div:has-text(Sponsored)    ẩn khối chứa chữ “Sponsored”", "example.com##div:has-text(Sponsored)    hide blocks that contain “Sponsored”"),
        ["custom.example4"] = ("@@||example.org^$document              không lọc trang này", "@@||example.org^$document              never filter this site"),
        ["custom.save"] = ("Lưu và áp dụng", "Save and apply"),
        ["custom.try"] = ("Thử một địa chỉ", "Try an address"),
        ["custom.try.hint"] = ("Xem các luật (kể cả luật của bạn) sẽ làm gì với một yêu cầu. Để trống ô trang = yêu cầu đến từ một trang web khác (bên thứ ba).", "See what the rules, yours included, would do with a request. Leave the page empty to test a request that comes from another website (third party)."),
        ["custom.try.url"] = ("Địa chỉ cần thử", "Address to test"),
        ["custom.try.page"] = ("Trang đang mở (không bắt buộc)", "Page it is loaded from (optional)"),
        ["custom.try.type"] = ("Loại", "Type"),
        ["custom.try.button"] = ("Thử", "Test"),
        ["custom.try.badurl"] = ("Hãy nhập địa chỉ đầy đủ, bắt đầu bằng http:// hoặc https://", "Enter a full address starting with http:// or https://"),
        ["custom.try.wait"] = ("Các luật vừa lưu đang được áp dụng, hãy thử lại sau ít giây.", "Your rules are still being applied, try again in a few seconds."),
        ["custom.try.noengine"] = ("Chưa có bộ lọc nào được nạp.", "No filters are loaded yet."),
        ["custom.try.allow"] = ("Cho qua", "Allowed"),
        ["custom.try.block"] = ("Bị chặn", "Blocked"),
        ["custom.try.redirect"] = ("Bị thay bằng nội dung vô hại", "Replaced with a harmless stand-in"),
        ["custom.try.rewrite"] = ("Cho qua nhưng đã bỏ bớt tham số: {0}", "Allowed, but parameters were removed: {0}"),
        ["custom.saved"] = ("Đã áp dụng lúc {0} · {1} dòng quy tắc", "Applied at {0} · {1} rule lines"),
        ["custom.box"] = ("Quy tắc của bạn", "Your rules"),

        ["settings.title"] = ("Cài đặt", "Settings"),
        ["settings.general"] = ("Chung", "General"),
        ["settings.language"] = ("Ngôn ngữ", "Language"),
        ["settings.language.auto"] = ("Tự động (theo Windows)", "Automatic (Windows language)"),
        ["settings.startwithwindows"] = ("Khởi động cùng Windows", "Start with Windows"),
        ["settings.startminimized"] = ("Khởi động thu nhỏ vào khay hệ thống", "Start minimized to the tray"),
        ["settings.filters"] = ("Bộ lọc", "Filters"),
        ["settings.autoupdate"] = ("Tự động cập nhật bộ lọc", "Update filters automatically"),
        ["settings.interval"] = ("Kiểm tra mỗi (giờ)", "Check every (hours)"),
        ["settings.popups"] = ("Chặn cửa sổ popup quảng cáo", "Block advertising pop-up windows"),
        ["settings.cosmetic"] = ("Ẩn các khối quảng cáo trong trang (lọc phần tử)", "Hide ad blocks inside pages (element hiding)"),
        ["settings.https"] = ("HTTPS", "HTTPS"),
        ["settings.filterhttps"] = ("Lọc trang HTTPS (cần chứng chỉ NeatShield)", "Filter HTTPS pages (needs the NeatShield certificate)"),
        ["settings.excluded"] = ("Trang không bị lọc (mỗi dòng một tên miền, ví dụ: ngan-hang.vn)", "Sites that are never filtered (one host per line, e.g. mybank.com)"),
        ["settings.network"] = ("Mạng", "Network"),
        ["settings.port"] = ("Cổng proxy cục bộ", "Local proxy port"),
        ["settings.save"] = ("Lưu cài đặt", "Save settings"),
        ["settings.saved"] = ("Đã lưu lúc {0}", "Saved at {0}"),
        ["settings.cert"] = ("Chứng chỉ", "Certificate"),
        ["settings.cert.none"] = ("Chưa tạo chứng chỉ. Nó sẽ được tạo khi bạn bật bảo vệ lần đầu.", "No certificate yet. It is created the first time you turn protection on."),
        ["settings.cert.trusted"] = ("Đã được Windows tin cậy · hết hạn {0}", "Trusted by Windows · expires {0}"),
        ["settings.cert.untrusted"] = ("CHƯA được tin cậy · hết hạn {0}", "NOT trusted · expires {0}"),
        ["settings.cert.install"] = ("Cài chứng chỉ", "Install certificate"),
        ["settings.cert.remove"] = ("Gỡ chứng chỉ", "Remove certificate"),
        ["settings.cert.folder"] = ("Mở thư mục chứng chỉ (cho Firefox)", "Open certificate folder (for Firefox)"),
        ["settings.cert.installfail"] = ("Không cài được: {0}", "Could not install: {0}"),
        ["settings.cert.removefail"] = ("Không gỡ được: {0}", "Could not remove: {0}"),
        ["settings.cert.folderfail"] = ("Không mở được thư mục: {0}", "Could not open the folder: {0}"),
        ["settings.cert.firefox"] = ("Chrome và Edge dùng kho chứng chỉ của Windows. Firefox có kho riêng: vào Settings → Privacy & Security → View Certificates → Authorities → Import, chọn tệp NeatShield-Root-CA.cer, hoặc bật security.enterprise_roots.enabled trong about:config.", "Chrome and Edge use the Windows certificate store. Firefox has its own: Settings → Privacy & Security → View Certificates → Authorities → Import, choose NeatShield-Root-CA.cer, or set security.enterprise_roots.enabled in about:config."),
        ["settings.trouble"] = ("Khắc phục sự cố", "Troubleshooting"),
        ["settings.resetproxy"] = ("Trả lại cài đặt proxy của Windows", "Restore the Windows proxy setting"),
        ["settings.diag.copy"] = ("Sao chép báo cáo chẩn đoán", "Copy diagnostics report"),
        ["settings.diag.folder"] = ("Mở thư mục nhật ký", "Open the log folder"),
        ["settings.diag.copied"] = ("Đã sao chép. Báo cáo không chứa mật khẩu hay luật riêng của bạn, nhưng hãy đọc lại trước khi gửi cho ai.", "Copied. The report holds no passwords and none of your own rules, but read it before you send it to anyone."),
        ["settings.diag.fail"] = ("Không làm được: {0}", "Could not do that: {0}"),
        ["settings.resetdone"] = ("Đã trả lại cài đặt proxy. Bảo vệ đang tắt.", "Proxy setting restored. Protection is off."),

        ["tray.open"] = ("Mở NeatShield", "Open NeatShield"),
        ["tray.pause"] = ("Tắt bảo vệ", "Turn protection off"),
        ["tray.resume"] = ("Bật bảo vệ", "Turn protection on"),
        ["tray.update"] = ("Cập nhật bộ lọc", "Update filters"),
        ["tray.quit"] = ("Thoát NeatShield", "Quit NeatShield"),
        ["tray.tip.on"] = ("NeatShield — đang bảo vệ", "NeatShield — protecting"),
        ["tray.tip.off"] = ("NeatShield — đã tắt", "NeatShield — off"),
    };

    private static string _language = "en";

    /// <summary>"vi" or "en".</summary>
    public static string Language
    {
        get => _language;
        set
        {
            var v = value == "vi" ? "vi" : "en";
            if (v == _language) return;
            _language = v;
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    public static event EventHandler? Changed;

    /// <summary>Culture for dates and numbers shown in the window (follows the language in use).</summary>
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(_language == "vi" ? "vi-VN" : "en-US");

    public static string T(string key)
    {
        if (!Texts.TryGetValue(key, out var t)) return key;
        return _language == "vi" ? t.vi : t.en;
    }

    public static string T(string key, params object[] args) =>
        string.Format(Culture, T(key), args);

    private static readonly Regex DownloadFile = new(@"^Downloading (.+) \((\d+)/(\d+)\)\.\.\.$", RegexOptions.Compiled);

    /// <summary>The progress lines of the filter updater come in English from the core; this shows them in the chosen language.</summary>
    public static string Progress(string coreMessage)
    {
        if (coreMessage == "Downloading manifest.json...") return T("progress.manifest");
        var m = DownloadFile.Match(coreMessage);
        return m.Success ? T("progress.file", m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value) : coreMessage;
    }
}
