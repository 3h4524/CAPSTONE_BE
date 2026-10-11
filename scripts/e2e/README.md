# Chạy test E2E và integration local

Mọi người chạy **cùng một lệnh**, không phân biệt Windows / macOS / Linux:

```bash
./scripts/e2e/run-all.sh
```

## Yêu cầu duy nhất

**Docker** — không cần cài Postgres, Redis, .NET hay bất cứ gì khác.

| Hệ điều hành | Cài gì |
|---|---|
| Windows | [Docker Desktop](https://docs.docker.com/desktop/install/windows-install/) · [Git for Windows](https://git-scm.com/download/win) (để có `bash`) · Python 3 · .NET 8 SDK · Node 22 + pnpm |
| macOS | [Docker Desktop](https://docs.docker.com/desktop/install/mac-install/) · `bash`, Python 3, .NET 8, Node 22 |
| Linux | [Docker Engine](https://docs.docker.com/engine/install/) · `bash`, Python 3, .NET 8, Node 22 |

Docker là khác biệt duy nhất giữa các OS. Script chạy trên **Git Bash** ở Windows nên không cần WSL và không cần PowerShell.

Trên Windows hãy mở **Git Bash** rồi chạy script, đừng chạy trong PowerShell.

## Các lệnh

```bash
./scripts/e2e/run-all.sh           # dựng hết rồi chạy unit + integration + e2e
./scripts/e2e/run-all.sh unit      # chỉ unit test — không cần Docker
./scripts/e2e/run-all.sh integ     # dựng rồi chạy integration
./scripts/e2e/run-all.sh e2e       # dựng rồi chạy e2e
./scripts/e2e/run-all.sh down      # dừng API, SMTP sink và xoá container
```

Chạy lại nhiều lần vẫn cho cùng kết quả.

## Nếu chưa cài Docker

Chạy `./scripts/e2e/run-all.sh unit` vẫn được — 1044 FE unit test không cần gì ngoài `pnpm install`.

## Trường hợp port bị chiếm

Postgres và Redis publish ra `55432` và `56379` (không phải `5432`/`6379`) vì `5432` thường đã có PostgreSQL của bạn ở Windows, và Docker **không báo lỗi** khi không bind được — container vẫn healthy nhưng không ai kết nối được.

Đổi port nếu cần:

```bash
TEST_PG_PORT=55440 TEST_REDIS_PORT=56380 ./scripts/e2e/run-all.sh
```

Script sẽ báo rõ nếu port đã bị chiếm trước khi khởi động.

## Kiến trúc

```
docker-compose.yml          postgres:18-alpine + redis:7-alpine, dữ liệu trong RAM
  ↓
bootstrap-postgres.sh       bật container, chờ healthy, xác nhận host kết nối được
  ↓
start-backend.sh            tạo DB, nạp schema, bật SMTP sink và API
  ↓
seed-seller.sh              register → verify (đọc token từ email) → login
```

| Database | Dùng cho | Xoá khi nào |
|---|---|---|
| `apcs_e2e` | E2E chạy thật | `run-all.sh down` |
| `apcs_test` | integration test | `docker compose down -v` |

Hai database tách nhau có chủ đích: E2E ghi dữ liệu thật (profile, batch, ticket) và sẽ làm hỏng các integration test đếm hoặc sắp xếp.

## Không gọi được dịch vụ bên ngoài

Các adapter Cloudinary, PayOS, Google Sign-In và SMTP thật đều bị thay bằng fake, và mọi HTTP outbound bị chặn. Ba bảo đảm độc lập:

1. Biến môi trường rỗng chặn `.env` thật trước khi host khởi động.
2. Ghi đè cấu hình ở tầng host.
3. Thay adapter qua DI + handler chặn outbound.

Có integration test assert cả ba. Ngoài ra `ALL_PROXY` trỏ về `127.0.0.1:1` làm mọi request ra ngoài chắc chắn thất bại.

## Log và artifact

| | |
|---|---|
| Log | `scripts/e2e/.run/{api,smtp,seed,start,schema}.log` |
| Seller credentials | `scripts/e2e/.run/seller.json` |
| Email gửi đi | `scripts/e2e/.run/mail.jsonl` |
| E2E report | `pnpm exec playwright show-report` ở `CAPSTONE_FE` |
| E2E ảnh lúc fail | `CAPSTONE_FE/test-results/` |

Cả `.run/` đã gitignore.