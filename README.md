# SpeedSaga API

ASP.NET Core backend for the SpeedSaga real-money puzzle gaming platform.

## Features

- JWT authentication and role-based authorization (`Player` role)
- FluentValidation on all request DTOs
- SQL Server database with 12 tables and 15 stored procedures
- Wallet, deposits (Razorpay), entry fees, rewards
- Single-player and two-player matchmaking
- Win-rate based level allocation engine
- SignalR hub for real-time gameplay (`/hubs/game`)
- Geoblocking middleware for restricted Indian states
- Hangfire background jobs (win-rate recalc, queue cleanup, bot scan)
- Swagger UI in Development

## Prerequisites

- .NET 10 SDK
- SQL Server 2019+ (LocalDB, Express, or full instance)

## Database Setup

1. Update the connection string in `appsettings.json` if needed.
2. Run the database script:

```powershell
sqlcmd -S YOUR_SERVER -E -i "Database\SpeedSagaDB.sql"
```

Or open `Database\SpeedSagaDB.sql` in SSMS and execute it.

This creates `SpeedSagaDB` with all tables, indexes, stored procedures, and seed data.

## Configuration

Edit `appsettings.json`:

| Setting | Description |
|---------|-------------|
| `ConnectionStrings:SpeedSagaDB` | SQL Server connection string |
| `Jwt:Key` | Secret key (min 32 characters) |
| `Jwt:Issuer` / `Jwt:Audience` | Token issuer and audience |
| `Razorpay:KeyId` / `Razorpay:KeySecret` | Razorpay payment gateway credentials |

## Run

```powershell
cd "d:\TEAM\SG New\SpeedSaga.API"
dotnet run
```

Swagger: `https://localhost:7xxx/swagger` (see console output for port)

## API Endpoints

### Auth (public)
| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/auth/register` | Register player |
| POST | `/api/auth/login` | Login and receive JWT |

### Wallet (JWT required)
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/wallet/balance` | Wallet balance and KYC status |
| POST | `/api/wallet/create-order` | Create Razorpay deposit order |
| POST | `/api/wallet/deposit` | Confirm deposit after payment |
| GET | `/api/wallet/transactions` | Transaction history |

### Game (JWT required)
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/game/level` | Allocate puzzle level |
| POST | `/api/game/start-single` | Start single-player paid session |
| POST | `/api/game/join-match` | Join two-player matchmaking |
| POST | `/api/game/result` | Submit game result |
| GET | `/api/game/replay/{sessionId}` | Get replay data |

### Player (JWT required)
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/player/dashboard` | Player stats, wallet, KYC |

### SignalR
- Hub URL: `/hubs/game?access_token=YOUR_JWT`

## Authorization

Protected endpoints require header:

```
Authorization: Bearer <jwt_token>
```

JWT claims include `playerId`, `contact`, `stateCode`, and role `Player`.

## Project Structure

```
SpeedSaga.API/
├── Authorization/     # Policies and claim types
├── Controllers/       # API controllers
├── Database/          # SQL setup script
├── Extensions/        # Helper extensions
├── Hubs/              # SignalR GameHub
├── Infrastructure/    # SQL connection factory
├── Middleware/        # Geoblock + exception handling
├── Models/            # DTOs and settings
├── Services/          # Business logic
└── Validators/        # FluentValidation rules
```
