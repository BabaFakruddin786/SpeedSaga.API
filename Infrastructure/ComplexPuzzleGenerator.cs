using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;

namespace SpeedSaga.API.Infrastructure;

/// <summary>
/// 32×32 board — four distinct quadrants, each with its own fill style.
/// Tier counts: Easy=30, Medium=50, Hard=80, SuperHard=120.
/// </summary>
public static class ComplexPuzzleGenerator
{
    public const int BoardSize = 32;
    public const int MinArrows = 30;

    enum QuadrantStyle { LanesVertical, LanesHorizontal, Spiral, Wave }

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        IncludeFields = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    static readonly ConcurrentDictionary<(string Tier, int Seed), string> JsonCache = new();

    public static int TargetForTier(string tier) => tier switch
    {
        "SuperHard" => 120,
        "Hard" => 80,
        "Medium" => 50,
        _ => 30
    };

    public static string ToJson(string tier, int seed)
        => JsonCache.GetOrAdd((tier, seed), _ => JsonSerializer.Serialize(GenerateDto(tier, seed), JsonOpts));

    public static object Generate(string tier, int seed)
    {
        var puzzle = GenerateDto(tier, seed);
        return new { cols = puzzle.cols, rows = puzzle.rows, arrows = puzzle.arrows };
    }

    public static PuzzleSolver.PuzzleDto GenerateDto(string tier, int seed)
        => GenerateDto(TargetForTier(tier), seed);

    public static PuzzleSolver.PuzzleDto GenerateDto(int targetArrows, int seed)
    {
        targetArrows = Math.Max(MinArrows, targetArrows);
        for (int attempt = 0; attempt < 8; attempt++)
        {
            var puzzle = BuildQuadrantBoard(targetArrows, seed + attempt * 7919);
            if (puzzle.arrows.Count == targetArrows && IsFullCoverage(puzzle)) return puzzle;
        }
        return BuildQuadrantBoard(targetArrows, seed);
    }

    static PuzzleSolver.PuzzleDto BuildQuadrantBoard(int targetArrows, int seed)
    {
        var rng = new Random(seed);
        var regions = FixedQuadrants(BoardSize);
        var styles = ShuffledStyles(rng);

        var areas = new List<int>(4);
        foreach (var r in regions) areas.Add(r.w * r.h);

        var budget = DistributeArrowBudget(targetArrows, areas);
        TuneSpiralBudget(budget, styles);

        var arrows = new List<PuzzleSolver.ArrowDto>(targetArrows);
        int id = 1;

        for (int i = 0; i < regions.Count; i++)
        {
            var built = BuildQuadrant(regions[i], budget[i], styles[i], rng, id);
            arrows.AddRange(built);
            id += built.Count;
        }

        return new PuzzleSolver.PuzzleDto { cols = BoardSize, rows = BoardSize, arrows = arrows };
    }

    static void TuneSpiralBudget(int[] budget, QuadrantStyle[] styles)
    {
        for (int i = 0; i < styles.Length; i++)
        {
            if (styles[i] != QuadrantStyle.Spiral || budget[i] <= 4) continue;
            int spare = budget[i] - 3;
            budget[i] = 3;
            budget[(i + 1) % 4] += spare / 2;
            budget[(i + 3) % 4] += spare - spare / 2;
        }
    }

    static QuadrantStyle[] ShuffledStyles(Random rng)
    {
        var styles = new[]
        {
            QuadrantStyle.LanesVertical,
            QuadrantStyle.LanesHorizontal,
            QuadrantStyle.Spiral,
            QuadrantStyle.Wave
        };
        for (int i = styles.Length - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (styles[i], styles[j]) = (styles[j], styles[i]);
        }
        return styles;
    }

    static List<Region> FixedQuadrants(int size)
    {
        int half = size / 2;
        return new List<Region>(4)
        {
            new Region(0, 0, half, half),
            new Region(half, 0, size - half, half),
            new Region(0, half, half, size - half),
            new Region(half, half, size - half, size - half)
        };
    }

    static List<PuzzleSolver.ArrowDto> BuildQuadrant(Region region, int arrowCount, QuadrantStyle style, Random rng, int startId)
    {
        if (arrowCount <= 0) return new List<PuzzleSolver.ArrowDto>();

        return style switch
        {
            QuadrantStyle.Spiral => BuildSpiralQuadrant(region, arrowCount, rng, startId),
            QuadrantStyle.Wave => BuildLaneQuadrant(region, arrowCount, verticalLanes: rng.Next(2) == 0, rng, startId, wave: true),
            QuadrantStyle.LanesVertical => BuildLaneQuadrant(region, arrowCount, verticalLanes: true, rng, startId, wave: false),
            _ => BuildLaneQuadrant(region, arrowCount, verticalLanes: false, rng, startId, wave: false)
        };
    }

    static List<PuzzleSolver.ArrowDto> BuildSpiralQuadrant(Region region, int arrowCount, Random rng, int startId)
    {
        var path = PeelSpiral(region, rng.Next(2) == 0);
        if (path.Count < region.w * region.h)
            path = SerpentineHorizontal(region, false);

        int offset = rng.Next(path.Count);
        if (offset > 0)
        {
            var rotated = new List<int[]>(path.Count);
            rotated.AddRange(path.GetRange(offset, path.Count - offset));
            rotated.AddRange(path.GetRange(0, offset));
            path = rotated;
        }

        return SplitIntoArrows(path, arrowCount, startId).arrows;
    }

    static List<PuzzleSolver.ArrowDto> BuildLaneQuadrant(Region region, int arrowCount, bool verticalLanes, Random rng, int startId, bool wave)
    {
        var arrows = new List<PuzzleSolver.ArrowDto>(arrowCount);
        int id = startId;

        if (verticalLanes)
        {
            int cAcc = region.c0;
            int baseW = region.w / arrowCount;
            int extra = region.w % arrowCount;
            for (int i = 0; i < arrowCount; i++)
            {
                int w = baseW + (i < extra ? 1 : 0);
                if (w <= 0) break;
                var strip = new Region(cAcc, region.r0, w, region.h);
                var path = wave ? WaveHorizontal(strip, rng) : SerpentineHorizontal(strip, (i + rng.Next(2)) % 2 == 0);
                if (path.Count != strip.w * strip.h) path = SerpentineHorizontal(strip, false);
                if (path.Count >= 2)
                {
                    arrows.Add(new PuzzleSolver.ArrowDto { id = id++, dir = DirFromPts(path), pts = path });
                }
                cAcc += w;
            }
        }
        else
        {
            int rAcc = region.r0;
            int baseH = region.h / arrowCount;
            int extra = region.h % arrowCount;
            for (int i = 0; i < arrowCount; i++)
            {
                int h = baseH + (i < extra ? 1 : 0);
                if (h <= 0) break;
                var strip = new Region(region.c0, rAcc, region.w, h);
                var path = wave ? WaveVertical(strip, rng) : SerpentineVertical(strip, (i + rng.Next(2)) % 2 == 0);
                if (path.Count != strip.w * strip.h) path = SerpentineVertical(strip, false);
                if (path.Count >= 2)
                {
                    arrows.Add(new PuzzleSolver.ArrowDto { id = id++, dir = DirFromPts(path), pts = path });
                }
                rAcc += h;
            }
        }

        return arrows;
    }

    readonly struct Region
    {
        public readonly int c0, r0, w, h;
        public Region(int c0, int r0, int w, int h) { this.c0 = c0; this.r0 = r0; this.w = w; this.h = h; }
    }

    static int[] DistributeArrowBudget(int totalArrows, List<int> areas)
    {
        int n = areas.Count;
        var counts = new int[n];
        int totalArea = 0;
        foreach (var a in areas) totalArea += a;

        int assigned = 0;
        for (int i = 0; i < n - 1; i++)
        {
            counts[i] = Math.Max(1, (int)Math.Round(totalArrows * (areas[i] / (double)totalArea)));
            assigned += counts[i];
        }
        counts[n - 1] = Math.Max(1, totalArrows - assigned);

        int sum = 0;
        foreach (var c in counts) sum += c;
        int diff = totalArrows - sum;
        int idx = 0;
        while (diff != 0 && n > 0)
        {
            if (diff > 0) { counts[idx % n]++; diff--; }
            else if (counts[idx % n] > 1) { counts[idx % n]--; diff++; }
            idx++;
            if (idx > n * 24) break;
        }
        return counts;
    }

    static List<int[]> SerpentineHorizontal(Region region, bool flipRows)
    {
        var pts = new List<int[]>(region.w * region.h);
        bool goRight = true;
        int rStart = flipRows ? region.r0 + region.h - 1 : region.r0;
        int rEnd = flipRows ? region.r0 - 1 : region.r0 + region.h;
        int rStep = flipRows ? -1 : 1;

        for (int r = rStart; r != rEnd; r += rStep)
        {
            if (goRight)
            {
                for (int c = region.c0; c < region.c0 + region.w; c++)
                    pts.Add(new[] { c, r });
            }
            else
            {
                for (int c = region.c0 + region.w - 1; c >= region.c0; c--)
                    pts.Add(new[] { c, r });
            }
            goRight = !goRight;
        }
        return pts;
    }

    static List<int[]> SerpentineVertical(Region region, bool flipCols)
    {
        var pts = new List<int[]>(region.w * region.h);
        bool goDown = true;
        int cStart = flipCols ? region.c0 + region.w - 1 : region.c0;
        int cEnd = flipCols ? region.c0 - 1 : region.c0 + region.w;
        int cStep = flipCols ? -1 : 1;

        for (int c = cStart; c != cEnd; c += cStep)
        {
            if (goDown)
            {
                for (int r = region.r0; r < region.r0 + region.h; r++)
                    pts.Add(new[] { c, r });
            }
            else
            {
                for (int r = region.r0 + region.h - 1; r >= region.r0; r--)
                    pts.Add(new[] { c, r });
            }
            goDown = !goDown;
        }
        return pts;
    }

    static List<int[]> PeelSpiral(Region region, bool inward)
    {
        var pts = new List<int[]>(region.w * region.h);
        int left = region.c0, right = region.c0 + region.w - 1;
        int top = region.r0, bottom = region.r0 + region.h - 1;

        while (left <= right && top <= bottom)
        {
            for (int c = left; c <= right; c++) pts.Add(new[] { c, top });
            top++;
            for (int r = top; r <= bottom; r++) pts.Add(new[] { right, r });
            right--;
            if (top <= bottom)
            {
                for (int c = right; c >= left; c--) pts.Add(new[] { c, bottom });
                bottom--;
            }
            if (left <= right)
            {
                for (int r = bottom; r >= top; r--) pts.Add(new[] { left, r });
                left++;
            }
        }

        if (!inward) pts.Reverse();
        return pts;
    }

    static List<int[]> WaveHorizontal(Region region, Random rng)
    {
        int strip = Math.Clamp(rng.Next(2, 4), 2, Math.Max(2, region.h));
        var pts = new List<int[]>(region.w * region.h);
        bool goRight = rng.Next(2) == 0;

        for (int rs = 0; rs < region.h; rs += strip)
        {
            int r1 = Math.Min(region.r0 + region.h, region.r0 + rs + strip);
            for (int r = region.r0 + rs; r < r1; r++)
            {
                if (goRight)
                {
                    for (int c = region.c0; c < region.c0 + region.w; c++)
                        pts.Add(new[] { c, r });
                }
                else
                {
                    for (int c = region.c0 + region.w - 1; c >= region.c0; c--)
                        pts.Add(new[] { c, r });
                }
                if (r < r1 - 1)
                    pts.Add(new[] { goRight ? region.c0 + region.w - 1 : region.c0, r + 1 });
                goRight = !goRight;
            }
        }
        return DedupePath(pts);
    }

    static List<int[]> WaveVertical(Region region, Random rng)
    {
        int strip = Math.Clamp(rng.Next(2, 4), 2, Math.Max(2, region.w));
        var pts = new List<int[]>(region.w * region.h);
        bool goDown = rng.Next(2) == 0;

        for (int cs = 0; cs < region.w; cs += strip)
        {
            int c1 = Math.Min(region.c0 + region.w, region.c0 + cs + strip);
            for (int c = region.c0 + cs; c < c1; c++)
            {
                if (goDown)
                {
                    for (int r = region.r0; r < region.r0 + region.h; r++)
                        pts.Add(new[] { c, r });
                }
                else
                {
                    for (int r = region.r0 + region.h - 1; r >= region.r0; r--)
                        pts.Add(new[] { c, r });
                }
                if (c < c1 - 1)
                    pts.Add(new[] { c + 1, goDown ? region.r0 + region.h - 1 : region.r0 });
                goDown = !goDown;
            }
        }
        return DedupePath(pts);
    }

    static List<int[]> DedupePath(List<int[]> pts)
    {
        if (pts.Count == 0) return pts;
        var next = new List<int[]>(pts.Count) { pts[0] };
        for (int i = 1; i < pts.Count; i++)
        {
            var a = next[^1];
            var b = pts[i];
            if (a[0] != b[0] || a[1] != b[1]) next.Add(b);
        }
        return next;
    }

    static (List<PuzzleSolver.ArrowDto> arrows, int nextId) SplitIntoArrows(List<int[]> path, int targetArrows, int startId)
    {
        var arrows = new List<PuzzleSolver.ArrowDto>(targetArrows);
        int total = path.Count;
        if (total < 2 || targetArrows <= 0) return (arrows, startId);

        int baseLen = total / targetArrows;
        int extra = total % targetArrows;
        int idx = 0;
        int id = startId;

        for (int a = 0; a < targetArrows; a++)
        {
            int len = baseLen + (a < extra ? 1 : 0);
            if (len < 2 || idx + len > total) break;

            var segment = path.GetRange(idx, len);
            idx += len;
            arrows.Add(new PuzzleSolver.ArrowDto { id = id++, dir = DirFromPts(segment), pts = segment });
        }

        return (arrows, id);
    }

    static bool IsFullCoverage(PuzzleSolver.PuzzleDto puzzle)
    {
        if (puzzle.cols != BoardSize || puzzle.rows != BoardSize) return false;
        var seen = new bool[BoardSize, BoardSize];
        int filled = 0;

        foreach (var arrow in puzzle.arrows)
        {
            foreach (var p in arrow.pts)
            {
                int c = p[0], r = p[1];
                if (c < 0 || c >= BoardSize || r < 0 || r >= BoardSize) return false;
                if (seen[c, r]) return false;
                seen[c, r] = true;
                filled++;
            }
        }

        return filled == BoardSize * BoardSize;
    }

    static string DirFromPts(List<int[]> pts)
    {
        if (pts.Count < 2) return "R";
        int dc = pts[^1][0] - pts[^2][0];
        int dr = pts[^1][1] - pts[^2][1];
        if (dc > 0) return "R";
        if (dc < 0) return "L";
        if (dr > 0) return "D";
        if (dr < 0) return "U";
        return "R";
    }
}
