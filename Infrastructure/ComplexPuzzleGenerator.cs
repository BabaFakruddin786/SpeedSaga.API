using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;

namespace SpeedSaga.API.Infrastructure;

/// <summary>Procedural dense puzzles: Easy=30, Medium=50, Hard=80, SuperHard=120.</summary>
public static class ComplexPuzzleGenerator
{
    public const int MinArrows = 30;

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
        => GenerateDto(Math.Max(MinArrows, TargetForTier(tier)), seed);

    /// <summary>Fast path for live API — client trusts server grid; skip expensive solvability search.</summary>
    public static PuzzleSolver.PuzzleDto GenerateDto(int targetArrows, int seed)
    {
        targetArrows = Math.Max(MinArrows, targetArrows);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            var puzzle = BuildOnce(targetArrows, seed + attempt * 7919);
            if (puzzle.arrows.Count >= targetArrows * 0.85) return puzzle;
        }
        return BuildSolubleFallback(targetArrows, seed);
    }

    static PuzzleSolver.PuzzleDto BuildOnce(int targetArrows, int seed)
    {
        var rng = new Random(seed);
        int size = GridSize(targetArrows);
        var occ = new bool[size, size];
        var arrows = new List<PuzzleSolver.ArrowDto>();
        int id = 1;
        int fails = 0;
        int maxFails = targetArrows * 40;

        while (arrows.Count < targetArrows && fails < maxFails)
        {
            if (TryPlace(occ, size, rng, id, out var arrow))
            {
                arrows.Add(arrow);
                id++;
                fails = 0;
            }
            else fails++;
        }

        while (arrows.Count < targetArrows && fails < maxFails)
        {
            if (TryPlace(occ, size, rng, id, out var arrow, 3, 6))
            {
                arrows.Add(arrow);
                id++;
                fails = 0;
            }
            else fails++;
        }

        return new PuzzleSolver.PuzzleDto { cols = size, rows = size, arrows = arrows };
    }

    static PuzzleSolver.PuzzleDto BuildSolubleFallback(int targetArrows, int seed)
    {
        int size = GridSize(targetArrows);
        var rng = new Random(seed);
        var arrows = new List<PuzzleSolver.ArrowDto>();
        var occ = new bool[size, size];
        int id = 1;

        for (int attempt = 0; attempt < size * size * 4 && arrows.Count < targetArrows; attempt++)
        {
            int c = rng.Next(1, size - 4);
            int r = rng.Next(1, size - 2);
            if (occ[c, r]) continue;

            string dir = NearestEdgeDir(c, r, size);
            var (dc, dr) = Delta(DirIndex(dir));
            var pts = new List<int[]> { new[] { c, r } };
            int len = rng.Next(3, 6);
            for (int s = 1; s < len; s++)
            {
                int nc = pts[^1][0] + dc;
                int nr = pts[^1][1] + dr;
                if (nc < 1 || nc >= size - 1 || nr < 1 || nr >= size - 1) break;
                if (occ[nc, nr]) break;
                pts.Add(new[] { nc, nr });
            }
            if (pts.Count < 3) continue;

            foreach (var p in pts) occ[p[0], p[1]] = true;
            arrows.Add(new PuzzleSolver.ArrowDto { id = id++, dir = DirFromPts(pts, dir), pts = pts });
        }

        var puzzle = new PuzzleSolver.PuzzleDto { cols = size, rows = size, arrows = arrows };
        if (puzzle.arrows.Count >= targetArrows * 0.85) return puzzle;
        return BuildOnce(targetArrows, seed + 99991);
    }

    static int GridSize(int arrows)
    {
        int side = (int)Math.Ceiling(Math.Sqrt(arrows * 8.5)) + 6;
        return Math.Clamp(side, 32, 58);
    }

    static bool TryPlace(bool[,] occ, int size, Random rng, int id, out PuzzleSolver.ArrowDto arrow, int minLen = 4, int maxLen = 12)
    {
        arrow = null!;
        for (int attempt = 0; attempt < 80; attempt++)
        {
            int sc = rng.Next(1, size - 1);
            int sr = rng.Next(1, size - 1);
            if (occ[sc, sr]) continue;

            var pts = new List<int[]> { new[] { sc, sr } };
            int dir = rng.Next(4);
            int len = rng.Next(minLen, maxLen + 1);

            for (int step = 1; step < len; step++)
            {
                if (rng.NextDouble() < 0.38)
                    dir = Turn(dir, rng.Next(2) == 0 ? 1 : -1);

                var (dc, dr) = Delta(dir);
                int nc = pts[^1][0] + dc;
                int nr = pts[^1][1] + dr;
                if (nc < 0 || nc >= size || nr < 0 || nr >= size) break;
                if (occ[nc, nr]) break;
                if (pts.Exists(p => p[0] == nc && p[1] == nr)) break;
                pts.Add(new[] { nc, nr });
            }

            if (pts.Count < 3) continue;
            foreach (var p in pts) occ[p[0], p[1]] = true;

            arrow = new PuzzleSolver.ArrowDto { id = id, dir = DirFromPts(pts, DirChar(dir)), pts = pts };
            return true;
        }
        return false;
    }

    static string NearestEdgeDir(int c, int r, int size)
    {
        int left = c, right = size - 1 - c, top = r, bottom = size - 1 - r;
        int min = Math.Min(Math.Min(left, right), Math.Min(top, bottom));
        if (min == left) return "L";
        if (min == right) return "R";
        if (min == top) return "U";
        return "D";
    }

    static int DirIndex(string dir) => dir switch { "R" => 0, "L" => 1, "U" => 2, _ => 3 };

    static (int dc, int dr) Delta(int dir) => dir switch
    {
        0 => (1, 0), 1 => (-1, 0), 2 => (0, -1), _ => (0, 1)
    };

    static int Turn(int dir, int sign)
    {
        int next = dir + sign;
        if (next < 0) return 3;
        if (next > 3) return 0;
        return next;
    }

    static string DirChar(int dir) => dir switch
    {
        0 => "R", 1 => "L", 2 => "U", _ => "D"
    };

    static string DirFromPts(List<int[]> pts, string fallback)
    {
        if (pts.Count < 2) return fallback;
        int dc = pts[^1][0] - pts[^2][0];
        int dr = pts[^1][1] - pts[^2][1];
        if (dc > 0) return "R";
        if (dc < 0) return "L";
        if (dr > 0) return "D";
        if (dr < 0) return "U";
        return fallback;
    }
}
