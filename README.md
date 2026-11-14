# NeatShield

Ứng dụng Windows chặn quảng cáo và theo dõi, kiểu AdGuard, xây trên hai repo:

- [`neatbrowser/neat-adblock`](https://github.com/neatbrowser/neat-adblock) — engine lọc (`neat_adblock.dll`, Rust, bọc adblock-rust).
- [`neatbrowser/neat-filters`](https://github.com/neatbrowser/neat-filters) — bộ luật lọc, phát hành tự động trên GitHub Releases.

> **Trạng thái:** v0.3. Lõi (`NeatShield.Core`, gồm cả proxy) đã được chạy trên Linux với **engine thật, bộ lọc neat-filters thật, thư viện Titanium.Web.Proxy thật và Chromium thật** (xem `tests/e2e`). **Ứng dụng WinUI, phần registry/DPAPI/cài chứng chỉ vào kho Windows và khay hệ thống chưa từng được biên dịch hay chạy** (môi trường viết code không có Windows), hãy coi chúng là code chưa kiểm thử. Xem mục “Đã kiểm tra gì”.

## Cách hoạt động

```
Trình duyệt / ứng dụng ──► proxy HTTP(S) cục bộ 127.0.0.1:8877 ──► Internet
                              │  (Titanium.Web.Proxy, chứng chỉ gốc riêng của máy này)
                              ▼
                         neat_adblock.dll  (neat_check_request, neat_cosmetic_resources, ...)
```

1. Bật bảo vệ → NeatShield mở proxy trên 127.0.0.1 và đặt nó làm proxy hệ thống của Windows (người dùng hiện tại). Cài đặt proxy cũ của bạn được lưu lại và **trả lại** khi tắt/thoát, và cả khi khởi động lại sau sự cố.
2. Với HTTPS, NeatShield giải mã bằng chứng chỉ gốc tạo trên chính máy này (khóa riêng được bảo vệ bằng DPAPI, không rời máy). Ngân hàng/đăng nhập Microsoft và các tên miền bạn loại trừ được cho đi thẳng, không giải mã.
3. Mỗi yêu cầu được engine kiểm tra: cho qua, chặn (trả phản hồi trống phù hợp loại: ảnh 1×1, script/CSS rỗng, 204…), hoặc thay bằng tài nguyên vô hại (luật `$redirect`). Tham số theo dõi (`$removeparam`) được bỏ khỏi URL.
4. Trang HTML được chèn CSS ẩn phần tử, script (scriptlet) của engine và một script nhỏ của NeatShield (`runtime.js`), kèm nonce vào Content-Security-Policy để trang không chặn chúng. Script này lo hai việc CSS không làm được:
   - **Luật ẩn dạng thủ tục** (`:has-text(...)`, `:upward(...)`, `:matches-path(...)`, `:matches-attr(...)`, `:remove()`, `:remove-attr(...)`): chạy phía trang, theo dõi thay đổi DOM (có giới hạn tần suất).
   - **Ẩn phần tử chung theo class/id**: script gom các class/id đang có trong trang, hỏi proxy (địa chỉ nội bộ `neatshield.invalid`, do proxy tự trả lời, không ra Internet) danh sách selector chung tương ứng từ engine (`neat_hidden_class_id_selectors`) rồi ẩn chúng.
   Luật chỉ cần CSS (kể cả `:style(...)`) vẫn được đổ thẳng vào CSS, không cần script.
5. **Bộ lọc tùy chọn của bạn** (trang “Bộ lọc của tôi”) được ghép thành một danh sách `custom.txt` (không tin cậy: không được dùng luật script-injection quyền cao) cạnh bản phát hành, trong một thư mục ghép bằng hard link; bản phát hành không bị sửa. Lưu là áp dụng ngay, không cần tải lại.
6. Bộ lọc tự cập nhật từ `neat-filters` (mặc định mỗi 6 giờ): chỉ tải tệp đổi (theo SHA-256), kiểm tra kích thước + hash, xây xong mới thay thế; engine được nạp lại không làm gián đoạn duyệt web.

## Giao diện

Trang chủ (công tắc lớn “Bảo vệ đang bật”, số liệu, nút **Kiểm tra bảo vệ**), Bộ lọc (phiên bản, cập nhật ngay), **Bộ lọc của tôi** (soạn luật riêng, ô **Thử một địa chỉ** để xem luật sẽ chặn hay cho qua), Thống kê (tên miền bị chặn nhiều nhất, nhật ký gần đây), Cài đặt (ngôn ngữ, cổng, chặn popup, ẩn phần tử, HTTPS, loại trừ, tự khởi động, chứng chỉ, khắc phục sự cố: “trả lại proxy Windows”, **sao chép báo cáo chẩn đoán**, mở thư mục nhật ký) và biểu tượng khay hệ thống (mở, bật/tắt, cập nhật, thoát). Đóng cửa sổ chỉ thu nhỏ vào khay.

**Kiểm tra bảo vệ** hỏi như một trình duyệt sẽ hỏi, nhưng không cần trình duyệt: đã nạp bộ lọc chưa, proxy có trả lời không, một kết nối HTTPS qua NeatShield có mở được không (chứng minh Windows tin chứng chỉ NeatShield), luật có chặn các địa chỉ quảng cáo nổi tiếng không, Windows có đang gửi lưu lượng tới NeatShield không. Không gửi gì ra Internet. **Báo cáo chẩn đoán** (phiên bản, trạng thái, chứng chỉ, cài đặt, 40 dòng nhật ký cuối) không chứa mật khẩu, luật riêng hay danh sách loại trừ (chỉ số lượng); đính kèm khi báo lỗi.

**Ngôn ngữ:** tiếng Việt và tiếng Anh (Cài đặt → Ngôn ngữ; mặc định theo ngôn ngữ giao diện Windows: tiếng Việt thì dùng tiếng Việt, còn lại tiếng Anh). Đổi là có hiệu lực ngay, kể cả menu khay và các trang “Đã bị chặn / Đã chặn popup” do proxy trả về. Mọi chữ nằm ở `src/NeatShield.App/Loc.cs` (khóa → (vi, en)); thêm ngôn ngữ mới = thêm một phần tử vào mỗi dòng. Thông báo lỗi từ `NeatShield.Core` (ví dụ lỗi chứng chỉ) vẫn là tiếng Anh.

**Bộ lọc của tôi** dùng cú pháp Adblock Plus / uBlock Origin, mỗi dòng một luật, `!` là ghi chú, dòng sai bị bỏ qua (không báo lỗi):

```
||ads.example.com^                      chặn một tên miền
example.com##.banner                    ẩn phần tử trên example.com
example.com##div:has-text(Sponsored)    ẩn khối chứa chữ “Sponsored”
@@||example.org^$document               không lọc trang này
```

## Cấu trúc

| Thư mục | Nội dung |
|---|---|
| `src/NeatShield.Core` | Toàn bộ logic: P/Invoke engine, tải/kiểm tra bộ lọc, proxy, chứng chỉ, proxy hệ thống, thống kê, `ShieldRuntime` (điểm ghép chung). Không phụ thuộc giao diện. |
| `src/NeatShield.App` | WinUI 3 (không đóng gói MSIX), các trang, khay hệ thống. |
| `src/NeatShield.Cli` | `neatshield run | status | update | check <url> | trust-ca | untrust-ca | reset-proxy`. |
| `tests/NeatShield.Core.Tests` | Bộ kiểm thử dạng console (không cần framework): `dotnet run --project tests/NeatShield.Core.Tests`. |
| `tests/e2e` | Kiểm thử đầu-cuối trên Linux: proxy thật + Chromium thật + site thử cục bộ (`bash tests/e2e/run.sh`, xem README trong thư mục). Có workflow chạy tay `e2e (manual)` trên GitHub (chưa chạy lần nào). |
| `tests/js` | Kiểm thử `runtime.js` (script chạy trong trang) bằng jsdom: `cd tests/js && npm install && npm test`. |
| `tools/` | `fetch-native.ps1`, `fetch-filters.ps1`, `build.ps1`. |

## Build (trên Windows)

Cần .NET SDK 10 (và Rust + MSVC nếu muốn tự build `neat_adblock.dll` bằng `-Build`).

```powershell
./tools/build.ps1            # tải neat_adblock.dll + bộ lọc, chạy test (C# và JS nếu có Node.js), publish ra dist\x64
./tools/build.ps1 -Arch arm64
```

Chạy `dist\x64\NeatShield.exe`. Lần đầu bật bảo vệ, Windows sẽ hỏi xác nhận cài chứng chỉ gốc của NeatShield cho người dùng hiện tại.

Nếu mất mạng sau khi app sập: chạy `neatshield reset-proxy` (hoặc nút “Trả lại cài đặt proxy của Windows”).

Dòng lệnh: `neatshield run [--no-system-proxy]` (chỉ mở proxy trên `127.0.0.1:<cổng>`, không đụng cài đặt proxy Windows: dùng cho một trình duyệt riêng, ví dụ `chrome --proxy-server=127.0.0.1:8877`), `selftest`, `diag`, `status`, `update`, `check <url> [loại] [trang]`, `trust-ca`, `reset-proxy`.

## Phát hành bằng tag

Workflow `.github/workflows/release.yml` chạy khi bạn đẩy một **tag bắt đầu bằng `v`**: nó build x64 và arm64 (có chạy test), nén thành `NeatShield-<tag>-win-<arch>.zip` kèm tệp `.sha256`, rồi tạo GitHub Release với ghi chú tự sinh.

```bash
git add -A
git commit -m "NeatShield 0.3.0"
git push                                   # đẩy mã lên nhánh chính trước

git tag -a v0.3.0 -m "NeatShield 0.3.0"    # tạo tag
git push origin v0.3.0                     # đẩy tag => workflow "release" chạy, tạo Release sau vài phút
```

- Tên tag phải có dạng `vX.Y.Z` (bản chính thức) hoặc `vX.Y.Z-rc.1`, `vX.Y.Z-beta.1` (có dấu `-` thì được đánh dấu **pre-release**). Số phiên bản sau chữ `v` được ghi vào ứng dụng (hiện trong báo cáo chẩn đoán).
- Nên thử lần đầu bằng một tag pre-release (`v0.3.0-rc.1`); nếu ổn thì tạo `v0.3.0`.
- Tag sai hoặc build lỗi: xóa Release (trang Releases) rồi xóa tag, sửa, tạo lại:
  `git tag -d v0.3.0 && git push origin :refs/tags/v0.3.0`
- Mỗi lần phát hành tăng số: sửa lỗi nhỏ `v0.3.1`, thêm tính năng `v0.4.0`, thay đổi lớn `v1.0.0`.
- Workflow `build` vẫn chạy ở mỗi lần push và chỉ tạo artifact (không tạo Release).

## Giới hạn đã biết

- **Firefox** dùng kho chứng chỉ riêng: nhập `NeatShield-Root-CA.cer` thủ công (hướng dẫn trong Cài đặt) hoặc bật `security.enterprise_roots.enabled`.
- Ứng dụng ghim chứng chỉ (certificate pinning) sẽ lỗi khi bị giải mã: thêm tên miền vào danh sách loại trừ. Một số tên miền Microsoft/Dropbox/Webex đã được loại sẵn.
- **Chặn popup** là phỏng đoán ở phía proxy: proxy không thấy “cửa sổ mới”, nên coi mọi điều hướng trang từ site khác (Sec-Fetch-Site: cross-site, hoặc Referer khác site) là ứng viên và hỏi danh sách popup của bộ lọc (`popups.txt`). Cửa sổ bị chặn tự đóng; nếu thực ra là bấm link trong cùng tab tới một tên miền có trong danh sách popup, bạn thấy trang “Pop-up blocked” và có thể quay lại hoặc thêm tên miền vào loại trừ. Không chặn được popup do script mở bằng `about:blank` rồi gán địa chỉ sau, hay popup mở từ trang không đi qua proxy.
- **Ẩn phần tử thủ tục / chung theo class/id** cần script chạy trong trang. Đã thử trong Chromium thật (CSP chặt, CSP không có `connect-src`, CSP đặt qua thẻ `<meta>`, gzip/brotli/chunked đều chạy), **chưa thử trong Firefox/Safari**. Script bị vô hiệu ở trang chặn cả nonce (hiếm). Nếu endpoint `neatshield.invalid` không tới được, luật thủ tục vẫn chạy, chỉ phần “chung theo class/id” bị mất.
- Ở trang không có `generichide`, engine trả hàng nghìn selector chung (~380 KB CSS) và NeatShield chèn tất cả vào mỗi trang HTML (tối đa 20 000 selector). Đo trên Linux: thêm khoảng 5–15 ms mỗi trang HTML; trình duyệt vẫn phải phân tích CSS đó. Nếu thấy chậm, tắt “Ẩn các khối quảng cáo trong trang” trong Cài đặt.
- Luật sai cú pháp trong “Bộ lọc của tôi” bị engine bỏ qua mà không báo (dùng ô “Thử một địa chỉ” để kiểm tra luật chặn; luật ẩn phần tử thì mở một trang thật).
- Chưa có: chạy như dịch vụ Windows, tự cài chứng chỉ cho Firefox, ngôn ngữ thứ ba.
- Chứng chỉ gốc có hiệu lực ~13 tháng; khi hết hạn app tạo cái mới và cần cài lại.
- Trình cập nhật không đi qua proxy hệ thống (để khỏi vòng lặp), nên mạng bắt buộc dùng proxy công ty sẽ không cập nhật được.

## Đã kiểm tra gì

| Phần | Cách kiểm tra |
|---|---|
| Đọc/kiểm tra manifest, tải bản phát hành thật, kiểm tra SHA-256, cache engine | Chạy thật với `neat-filters` release thật (lần 2 = “up to date”, nạp lại từ cache) |
| Engine qua P/Invoke | Chạy thật với `libneat_adblock.so` build từ repo (chặn/redirect/CSP/cosmetic) |
| Phân loại request, luật host, CSP nonce, chèn HTML (cả mức byte), data: URL, cài đặt, DPAPI-wrapper (nhánh không-Windows) | `tests/NeatShield.Core.Tests` — chạy qua |
| Chặn popup (nhận diện điều hướng cross-site, danh sách `popups.txt` thật), luật ẩn thủ tục + selector chung (`CosmeticBuilder`) | `tests/NeatShield.Core.Tests` + chạy với engine thật và bộ luật thật |
| `runtime.js` (`:has-text`, `:upward`, `:matches-path`, `:matches-attr`, `:remove`, `:remove-attr`, ẩn theo class/id, giới hạn tần suất, chạy một lần mỗi cửa sổ) | `tests/js` — 11 test trong jsdom; payload thật do engine thật sinh ra cũng được nạp vào jsdom |
| Bộ lọc tùy chọn (`ComposedRelease`, nạp lại engine, luật tùy chọn chặn/ẩn thật qua engine) | `tests/NeatShield.Core.Tests` + chạy với engine thật |
| Văn bản vi/en (khóa đủ hai ngôn ngữ, tham số `{n}` khớp nhau, đổi ngôn ngữ, trang chặn/popup) | `tests/NeatShield.Core.Tests` |
| `ProxyService`, `CertificateService`, `ShieldRuntime`, CLI (Titanium 8.0.2 biên dịch từ mã nguồn, BouncyCastle từ mã nguồn) | `tests/e2e` + chạy tay trên Linux: HTTP/HTTPS giải mã, chứng chỉ upstream tự ký bị từ chối (502), chặn host, trang chặn, WebSocket, SSE, POST 5 MB, tải 300 MB (~500 MB/s, RAM không tăng), 200 yêu cầu song song, `selftest` |
| Chromium thật qua proxy: ẩn phần tử, `:has-text`, ẩn theo class/id qua `neatshield.invalid`, CSP (header, thiếu connect-src, thẻ meta), nén, popup | `tests/e2e/check.js` — 45 kiểm tra đều đạt |
| `SystemProxy` (registry), `StartupRegistration`, DPAPI, cài chứng chỉ vào kho Windows, WinUI App (gồm các trang mới, `L.cs`, đổi ngôn ngữ tại chỗ, nút kiểm tra, ô thử địa chỉ), khay | **Chưa biên dịch, chưa chạy** (cần Windows) |
| Firefox, Edge/Chrome trên Windows | **Chưa thử** (Chromium trên Linux đã thử) |
