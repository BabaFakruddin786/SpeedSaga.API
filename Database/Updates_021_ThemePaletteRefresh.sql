USE SpeedSagaDB;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- Premium gaming palettes — rich contrast, vivid gradients, eye-catching CTAs.

UPDATE AppThemes SET
    ThemeName = N'Royal Night',
    Bg = N'#08080E', Surface = N'#111119', Card = N'#1A1A28', Border = N'#32324A',
    Gold = N'#FFCE00', GoldDim = N'#B8940F', Accent = N'#9333EA', AccentBright = N'#A855F7',
    Green = N'#22D3A5', Red = N'#FF4D6A', Orange = N'#FF8C42',
    TextColor = N'#F8F8FF', TextMuted = N'#9898B8', TextDim = N'#55556E',
    WalletTop = N'#4A1D96', WalletBottom = N'#120A24',
    FreeModeTop = N'#0B5039', FreeModeBottom = N'#062E22',
    PremiumTop = N'#3B0764', PremiumBottom = N'#18042A',
    UpdatedAt = SYSUTCDATETIME()
WHERE ThemeCode = N'dark_premium';
GO

UPDATE AppThemes SET
    ThemeName = N'Luminous Arena',
    Bg = N'#F4F3F8', Surface = N'#FFFFFF', Card = N'#FFFFFF', Border = N'#E0E0EA',
    Gold = N'#B45309', GoldDim = N'#92400E', Accent = N'#7C3AED', AccentBright = N'#9333EA',
    Green = N'#059669', Red = N'#DC2626', Orange = N'#EA580C',
    TextColor = N'#18181B', TextMuted = N'#52525B', TextDim = N'#71717A',
    WalletTop = N'#7C3AED', WalletBottom = N'#5B21B6',
    FreeModeTop = N'#059669', FreeModeBottom = N'#047857',
    PremiumTop = N'#6D28D9', PremiumBottom = N'#4C1D95',
    UpdatedAt = SYSUTCDATETIME()
WHERE ThemeCode = N'light_white';
GO

UPDATE AppThemes SET
    ThemeName = N'Cyber Pulse',
    Bg = N'#0A0F1C', Surface = N'#111827', Card = N'#1A2235', Border = N'#334155',
    Gold = N'#38BDF8', GoldDim = N'#0284C7', Accent = N'#818CF8', AccentBright = N'#A5B4FC',
    Green = N'#2DD4BF', Red = N'#FB7185', Orange = N'#FB923C',
    TextColor = N'#F1F5F9', TextMuted = N'#94A3B8', TextDim = N'#64748B',
    WalletTop = N'#1E3A5F', WalletBottom = N'#0A1628',
    FreeModeTop = N'#134E4A', FreeModeBottom = N'#0F3330',
    PremiumTop = N'#312E81', PremiumBottom = N'#1E1B4B',
    UpdatedAt = SYSUTCDATETIME()
WHERE ThemeCode = N'dark_blue';
GO

UPDATE AppThemes SET
    ThemeName = N'Sunset Glow',
    Bg = N'#FDF8F3', Surface = N'#FFFBF7', Card = N'#FFF7ED', Border = N'#FED7AA',
    Gold = N'#EA580C', GoldDim = N'#C2410C', Accent = N'#DB2777', AccentBright = N'#EC4899',
    Green = N'#16A34A', Red = N'#BE123C', Orange = N'#F97316',
    TextColor = N'#431407', TextMuted = N'#78716C', TextDim = N'#A8A29E',
    WalletTop = N'#EA580C', WalletBottom = N'#9A3412',
    FreeModeTop = N'#15803D', FreeModeBottom = N'#166534',
    PremiumTop = N'#BE185D', PremiumBottom = N'#831843',
    UpdatedAt = SYSUTCDATETIME()
WHERE ThemeCode = N'soft_light';
GO

PRINT 'SpeedSaga DB update 021 — premium theme palettes applied.';
GO
