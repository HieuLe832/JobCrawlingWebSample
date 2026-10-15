# IT Job Crawler

Hệ thống crawl việc làm IT từ các trang tuyển dụng Việt Nam và trang careers của một số công ty, sau đó **phân loại** từng job theo mảng (Backend, Data/AI, QA…) và cấp bậc (Intern → Manager).

Đây là bản prototype để kiểm tra tính khả thi. Dự án chỉ có 2 chức năng: **crawl** và **phân loại**.

- Backend: C# ASP.NET Core 8 Web API, AngleSharp, EF Core + SQLite
- Frontend: React 18 + Vite

## Nguồn dữ liệu

Chỉ crawl những đường dẫn mà robots.txt của site cho phép. Robots.txt được tải trực tiếp từ site mỗi lần chạy; thư mục `robots.txt_here/` chỉ là bản lưu để tham khảo.

| Nguồn | Cách lấy dữ liệu | Trạng thái |
|---|---|---|
| ITViec | HTML danh sách `/it-jobs` | ✅ |
| TopCV | HTML danh mục Công nghệ thông tin | ✅ |
| TopDev | Sitemap → trang chi tiết job (JSON-LD) | ✅ Chậm: phải mở từng job, và ~30% link trong sitemap là job đã hết hạn |
| VietnamWorks | API tìm kiếm mà chính web của họ dùng, lọc ngành IT | ✅ |
| TMA Solutions | HTML trang tuyển dụng | ✅ |
| Axon Active | RSS của blog tuyển dụng (Wix) | ✅ |
| NAB Vietnam | API Eightfold `/api/pcsx` (nằm trong danh sách robots cho phép), lọc Việt Nam | ✅ |
| Netcompany | HTML trang vacancies, chỉ giữ job ở Việt Nam | ✅ |
| FPT Software | — | ❌ Mọi request đều bị Cloudflare chặn bằng bot challenge. Không lách cơ chế chống bot nên không crawl |

TopDev không dùng `api.topdev.vn`, vì robots.txt của host đó là `Disallow: /`.

Kết quả một lần chạy thử ngày 2026-10-15: 577 job, trong đó 481 IT và 96 không phải IT. Các job không phải IT chủ yếu là sales, marketing, thiết kế đồ hoạ, nghiệp vụ ngân hàng.

## Cài đặt và chạy

Yêu cầu: .NET 8 SDK, Node.js 18+.

```bash
# Backend: http://localhost:5111 (Swagger tại /swagger)
cd backend/JobCrawler.Api
dotnet run --urls http://localhost:5111

# Frontend: http://localhost:5173 (tự proxy /api sang backend)
cd frontend
npm install
npm run dev
```

Database SQLite (`backend/JobCrawler.Api/jobs.db`) được tạo tự động khi chạy lần đầu. Nếu sửa model `JobPosting` thì xoá file này để tạo lại.

## Cách dùng

1. Mở http://localhost:5173.
2. **Crawl:** chọn nguồn và số trang cho mỗi nguồn, rồi bấm **Bắt đầu crawl**. Bảng trạng thái cập nhật mỗi 2 giây; job được lưu theo từng batch nên danh sách hiện dần trong lúc crawl.
3. **Phân loại:** bấm vào chip Mảng / Cấp bậc / Nguồn để lọc, hoặc tìm theo tiêu đề, công ty, kỹ năng. Mặc định chỉ hiện job IT; tick "Hiện cả job không phải IT" để xem phần còn lại.

Một lần crawl 3 trang mỗi nguồn mất vài giây với hầu hết các site, riêng TopDev khoảng 1–2 phút.

## API

| Method | Endpoint | Mô tả |
|---|---|---|
| `POST` | `/api/crawl` | Bắt đầu crawl chạy nền. Body: `{"sources": ["itviec", "topcv"], "maxPages": 2}`. Bỏ `sources` thì crawl tất cả. Trả về `409` nếu đang có crawl khác chạy |
| `GET` | `/api/crawl/status` | Trạng thái từng nguồn: số job đã lấy, mới, cập nhật, lỗi |
| `POST` | `/api/crawl/cancel` | Huỷ crawl đang chạy |
| `GET` | `/api/jobs` | Danh sách job. Tham số: `source`, `category`, `level`, `q`, `includeNonIt`, `page`, `pageSize` |
| `GET` | `/api/jobs/stats` | Số job theo mảng, cấp bậc và nguồn |
| `POST` | `/api/jobs/reclassify` | Chạy lại bộ phân loại cho toàn bộ job trong DB |

Mã nguồn dùng trong `sources`: `itviec`, `topcv`, `topdev`, `vietnamworks`, `tma`, `axonactive`, `nab`, `netcompany`.

```bash
curl -X POST localhost:5111/api/crawl -H 'Content-Type: application/json' -d '{"maxPages": 2}'
curl localhost:5111/api/crawl/status
curl 'localhost:5111/api/jobs?category=Backend&level=Senior'
```

## Phân loại hoạt động thế nào

Bộ phân loại dùng luật từ khoá có trọng số (`Classification/JobClassifier.cs`). Không dùng machine learning, nên cùng dữ liệu luôn ra cùng kết quả.

1. **Tính điểm theo từ khoá:**
   - từ chỉ vai trò trong tiêu đề (tester, business analyst, devops…): 6 điểm;
   - từ chỉ công nghệ trong tiêu đề (python, react…): 3 điểm;
   - danh mục/phòng ban của site và kỹ năng: 2 điểm;
   - mô tả: 1 điểm.
2. **IT hay không phải IT:**
   - Tiêu đề có từ của nghề không phải IT (sales, kế toán, thiết kế đồ hoạ, CNC…) và không có vai trò IT nào thì là Non-IT.
   - Ngoài ra, job là IT nếu nguồn chỉ đăng job IT hoặc tổng điểm từ 4 trở lên.
3. **Mảng và cấp bậc:**
   - Mảng là mảng có điểm cao nhất trong 14 mảng. Không khớp mảng nào thì ra "Other IT".
   - Cấp bậc lấy theo thứ tự ưu tiên: từ khoá trong tiêu đề, rồi cấp bậc site ghi, rồi số năm kinh nghiệm.

**Khi nào phân loại chạy:**
- **Mỗi lần crawl:** chạy cho các job vừa lấy về, kể cả job đã có trong DB.
- **Khi bấm "Phân loại lại"** (hoặc gọi `POST /api/jobs/reclassify`): chạy cho toàn bộ DB.
- **Không chạy định kỳ.** Sau khi sửa luật thì build lại, restart API, rồi bấm "Phân loại lại".

## Crawl có trách nhiệm

- Mọi request đều đi qua `PoliteHttpClient`, với các luật sau:
  - kiểm tra robots.txt theo RFC 9309;
  - mỗi host cách nhau 1,5 giây, gửi tuần tự;
  - tự thử lại khi gặp 429/5xx;
  - User-Agent ghi rõ là bot (`JobCrawlerBot`).
- Robots.txt trả về Cloudflare challenge thì coi như cấm crawl toàn bộ.
- Không crawl phần cần đăng nhập. Vì vậy cột lương trên ITViec và TopDev để trống.

Có thể chỉnh độ trễ, User-Agent và timeout trong `appsettings.json` → `Crawler`.

## Cấu trúc thư mục

```
backend/JobCrawler.Api/
  Crawling/PoliteHttpClient.cs   # HTTP: robots.txt, giãn cách, retry
  Crawling/RobotsTxt.cs          # Parser robots.txt
  Crawling/Sources/              # Mỗi nguồn một class IJobSource
  Crawling/CrawlCoordinator.cs   # Chạy crawl nền, lưu và phân loại
  Classification/JobClassifier.cs
  Controllers/                   # /api/crawl, /api/jobs
frontend/src/                    # Dashboard React
robots.txt_here/                 # Bản lưu robots.txt của từng site
CLAUDE.md                        # Quy ước code và các lỗi dễ gặp
```

Thêm nguồn mới:
1. Viết class implement `IJobSource` trong `Crawling/Sources/`.
2. Đăng ký class đó trong `Program.cs`.
3. Thêm mã nguồn vào `frontend/src/sources.js`.

Đọc robots.txt của **mọi** host sẽ gọi, kể cả subdomain API, trước khi viết.

## Hạn chế

- **Selector HTML phụ thuộc giao diện hiện tại của từng site.** Khi site đổi giao diện, nguồn đó sẽ trả về 0 job.
- **API nội bộ có thể đổi bất cứ lúc nào.** TopDev và VietnamWorks phụ thuộc vào sitemap và API nội bộ.
- **Phân loại bằng luật vẫn còn sai lẻ tẻ.** Lỗi hay gặp là tiếng Việt không dấu trùng với từ viết tắt tiếng Anh, chi tiết trong `CLAUDE.md`.
- **Job hết hạn vẫn nằm trong DB.** Chưa có crawl theo lịch, chưa lưu lịch sử nhãn, và chưa ẩn job cũ.
