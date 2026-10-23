# Chạy test E2E và integration local

Mọi người chạy **cùng một lệnh**, không phân biệt Windows / macOS / Linux:

```bash
E2E_ORCHESTRATION_COMMAND=test dotnet test tests/APCS.E2E.Automation/APCS.E2E.Automation.csproj
```

Nút **Run** trên trang admin dùng đúng lệnh này (xem `API/appsettings.json` → `TestReports:RunnerCommand`).

## Yêu cầu duy nhất

**Docker**, cộng với Python 3 (SMTP sink), .NET 8 SDK, và một lần `pnpm install` + Playwright browsers ở `CAPSTONE_FE`:

```bash
cd ../CAPSTONE_FE
pnpm install
pnpm exec playwright install chromium
```

## Các mode

Biến `E2E_ORCHESTRATION_COMMAND` điều khiển hành vi của test project:

| Mode | Làm gì |
|---|---|
| `test` (mặc định) | Dựng infra → schema từ EF model → start SMTP sink + API → tự seed seller (register → verify qua mail → login) → chạy Playwright suite → trả report |
| `up` | Chỉ dựng infra + API, giữ mọi thứ chạy để debug bằng tay |
| `down` | Dừng API + SMTP sink, `docker compose down -v` |
| `clean` | Xoá sạch database `apcs_e2e` |

Chạy lại nhiều lần vẫn cho cùng kết quả: schema dựng bằng `EnsureDeleted + EnsureCreated`, seller seed idempotent (đã tồn tại thì đăng nhập luôn).

Integration test **không chạy qua** project này — `dotnet test Capstone.sln` như thường; fixture tự dựng Postgres/Redis + schema từ EF model qua Testcontainers.

## Trường hợp port bị chiếm

| Dịch vụ | Port mặc định | Đổi bằng biến |
|---|---|---|
| Postgres + pgvector | `55432` | `E2E_TEST_PG_PORT` (`docker-compose.yml` dùng cùng biến) |
| Redis | `56379` | `E2E_TEST_REDIS_PORT` |
| API | tự chọn từ `5191..5195` | `E2E_API_PORT` |
| SMTP sink | `2525` | `E2E_SMTP_PORT` |

Neon dev server đang chạy cục bộ không bị ảnh hưởng: orchestrator chỉ đụng port rảnh, không có lệnh `pkill`.

## Kiến trúc

```
tests/APCS.E2E.Automation    MSTest SDK project (biến E2E_ORCHESTRATION_COMMAND)
  ├── E2EOrchestrator        lock file → compose up → schema (EF model) → SMTP sink → API → seed seller → Playwright
  ├── SellerSeeder           register → đọc token từ mail.jsonl → verify-email → login
  └── E2EOptions             env-driven cấu hình (port, email seller, timeout)
docker-compose.yml           pgvector/pgvector:pg18 + redis:7-alpine, dữ liệu trong RAM
smtp-sink.py                 SMTP giả, ghi JSONL ra .run/mail.jsonl
```

| Database | Dùng cho | Xoá khi nào |
|---|---|---|
| `apcs_e2e` | E2E chạy thật | `clean` mode hoặc `down` |
| `apcs_test` | integration test (Testcontainers) | container bị xoá ngay sau run |

## Không gọi được dịch vụ bên ngoài

Các adapter Cloudinary, PayOS, Google Sign-In và SMTP thật đều bị thay bằng stub creds
(`apcs-e2e-stub`), và `NO_PROXY` set rỗng cho localhost. E2E runner không chuyển khoá thật nào sang API.

## Log và artifact

| | |
|---|---|
| Log API / SMTP | `scripts/e2e/.run/{api,smtp}.log` |
| PID files | `scripts/e2e/.run/{api,smtp}.pid` |
| Email gửi đi | `scripts/e2e/.run/mail.jsonl` |
| E2E report | `CAPSTONE_FE/test-results/report/report.json` + `pnpm exec playwright show-report` |
| Ảnh lúc fail | `CAPSTONE_FE/test-results/` |

Cả `.run/` đã gitignore; lock `orchestration.lock` chặn hai orchestrator chạy đồng thời.
