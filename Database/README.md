# SpeedSaga database migrations

Apply **once** on a new SQL Server database, or after pulling new migration files.

## Quick apply (recommended)

From PowerShell:

```powershell
cd "d:\TEAM\SG New\SpeedSaga.API\Database"
# Existing database (most dev machines):
.\RunAllMigrations.ps1 -UpdatesOnly
# Brand-new empty database:
.\RunAllMigrations.ps1
```

Custom server:

```powershell
.\RunAllMigrations.ps1 -Server "localhost\SQLEXPRESS01" -Database SpeedSagaDB
```

## Production install (SSMS — one click)

1. Connect SSMS to your server (e.g. `187.127.134.76,1433`).
2. Enable **Query → SQLCMD Mode**.
3. Open `INSTALL_SpeedSaga_Production.sql` from this folder (all `Updates_*.sql` files must stay in the same folder).
4. Execute (F5). Fresh database only.

Default admin accounts are seeded at the end (`Production_SeedAdminUsers.sql`):

| Email | Password | Role |
|-------|----------|------|
| `admin@speedsaga.com` | `Admin@123456` | SuperAdmin |
| `support@speedsaga.com` | `Support@123456` | Support |

Change these passwords after first login.

## Manual apply (SSMS)

1. Create database `SpeedSagaDB` if it does not exist.
2. Run `SpeedSagaDB.sql`.
3. Run every `Updates_*.sql` file **in numeric order** (002 → 039).

## Migration index

| File | Purpose |
|------|---------|
| `SpeedSagaDB.sql` | Base tables + core stored procedures |
| `Updates_002` | Password reset tokens, withdrawals, notifications |
| `Updates_003` | Unique index fixes, register/login SPs |
| `Updates_004` | Playable puzzle seed data |
| `Updates_005` | Tournaments, KYC, match status, level seed |
| `Updates_006` | Level history, session moves |
| `Updates_007` | Scale indexes, batch move recording |
| `Updates_008` | Hard difficulty levels |
| `Updates_009` | Puzzle tiers |
| `Updates_010` | Arrow count tier data |
| `Updates_011` | Tier documentation (no schema) |
| `Updates_012` | KYC document SP |
| `Updates_013` | OTP + outgoing messages |
| `Updates_014` | Promos, tournament session columns |
| `Updates_015` | Game history time limit |
| `Updates_016` | Registration geoblock |
| `Updates_017` | Restore level tiers |
| `Updates_018` | Exclude practice from history |
| `Updates_019` | Withdraw-only geoblock |
| `Updates_020` | App themes |
| `Updates_021` | Theme palette refresh |
| `Updates_022` | Profile contact linking |
| `Updates_024` | Game play config tables |
| `Updates_025` | Free play time modes |
| `Updates_026` | Game play lives table |
| `Updates_027` | KYC document review columns |
| `Updates_028` | Support chat |
| `Updates_029` | KYC admin review |
| `Updates_030` | App support config (`USP_GetAppSupportConfig`) |
| `Updates_031` | App ticker config (`USP_GetAppTickerConfig`) |
| `Updates_032` | `GameType` on sessions/queue + matchmaking SP fix |

> Note: there is no `Updates_023`; numbering jumps from 022 to 024.

## Common errors if migrations were skipped

| Error | Fix |
|-------|-----|
| `Could not find stored procedure 'USP_GetAppTickerConfig'` | Run `Updates_031` |
| `Could not find stored procedure 'USP_GetAppSupportConfig'` | Run `Updates_030` |
| `Invalid column name 'GameType'` on game start | Run `Updates_032` |
| `Procedure USP_MatchmakingJoin has too many arguments` | Run `Updates_032` |
| OTP / forgot password fails | Run `Updates_013` |
| KYC upload fails | Run `Updates_027` + `Updates_029` |

## Dev utility

`Dev_ClearAllPlayerData.sql` — wipes player/wallet/session data only (not schema). Dev use only.
