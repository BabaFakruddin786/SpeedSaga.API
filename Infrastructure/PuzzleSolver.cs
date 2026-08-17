using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SpeedSaga.API.Infrastructure;

/// <summary>Verifies puzzles are solvable — no dead-end arrow layouts.</summary>
public static class PuzzleSolver
{
    const int MaxDepth = 600;
    const int MaxStates = 120_000;

    public sealed class PuzzleDto
    {
        public int cols;
        public int rows;
        public List<ArrowDto> arrows = new();
    }

    public sealed class ArrowDto
    {
        public int id;
        public string dir = "R";
        public List<int[]> pts = new();
    }

    public static bool IsSolvable(PuzzleDto data)
    {
        if (data?.arrows == null || data.arrows.Count == 0) return false;
        var engine = new SimEngine(data);
        var visited = new HashSet<string>();
        return Solve(engine, visited, 0);
    }

    static bool Solve(SimEngine engine, HashSet<string> visited, int depth)
    {
        if (engine.IsCleared()) return true;
        if (depth > MaxDepth || visited.Count > MaxStates) return false;
        if (!visited.Add(engine.StateKey())) return false;

        var movable = engine.Live().Where(a => engine.CanSlide(a.id)).ToList();
        if (movable.Count == 0) return false;

        foreach (var arrow in movable.OrderByDescending(a => engine.SlideClears(a.id) ? 1 : 0))
        {
            var branch = engine.Clone();
            branch.SlideUntilStop(arrow.id);
            if (Solve(branch, visited, depth + 1)) return true;
        }
        return false;
    }

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

    sealed class SimArrow
    {
        public int id;
        public string dir = "R";
        public List<(int x, int y)> cells = new();
        public bool cleared;
    }

    sealed class SimEngine
    {
        public int Cols { get; }
        public int Rows { get; }
        readonly List<SimArrow> _arrows;

        public SimEngine(PuzzleDto data)
        {
            Cols = data.cols;
            Rows = data.rows;
            _arrows = data.arrows.Select(a => new SimArrow
            {
                id = a.id,
                dir = DirFromPts(a.pts, a.dir),
                cells = a.pts.Select(p => (p[0], p[1])).ToList()
            }).ToList();
        }

        SimEngine(int cols, int rows, List<SimArrow> arrows)
        {
            Cols = cols;
            Rows = rows;
            _arrows = arrows;
        }

        public IEnumerable<SimArrow> Live() => _arrows.Where(a => !a.cleared);
        public bool IsCleared() => _arrows.All(a => a.cleared);

        public bool CanSlide(int id)
        {
            var a = _arrows.FirstOrDefault(x => x.id == id && !x.cleared);
            return a != null && CanSlideArrow(a);
        }

        bool CanSlideArrow(SimArrow arrow)
        {
            var (dc, dr) = Delta(arrow.dir);
            var head = arrow.cells[^1];
            int nc = head.x + dc, nr = head.y + dr;
            if (nc < 0 || nc >= Cols || nr < 0 || nr >= Rows) return true;
            foreach (var other in Live())
            {
                if (other.id == arrow.id) continue;
                if (other.cells.Any(c => c.x == nc && c.y == nr)) return false;
            }
            return true;
        }

        public bool SlideClears(int id)
        {
            var probe = Clone();
            return probe.SlideUntilStop(id);
        }

        public bool SlideUntilStop(int id)
        {
            var arrow = _arrows.FirstOrDefault(a => a.id == id && !a.cleared);
            if (arrow == null) return false;
            while (CanSlideArrow(arrow))
            {
                var (dc, dr) = Delta(arrow.dir);
                for (int i = 0; i < arrow.cells.Count; i++)
                    arrow.cells[i] = (arrow.cells[i].x + dc, arrow.cells[i].y + dr);
                if (arrow.cells.All(c => c.x < 0 || c.x >= Cols || c.y < 0 || c.y >= Rows))
                {
                    arrow.cleared = true;
                    return true;
                }
            }
            return false;
        }

        public SimEngine Clone()
        {
            var arrows = _arrows.Select(a => new SimArrow
            {
                id = a.id,
                dir = a.dir,
                cleared = a.cleared,
                cells = a.cells.ToList()
            }).ToList();
            return new SimEngine(Cols, Rows, arrows);
        }

        public string StateKey()
        {
            var sb = new StringBuilder(256);
            foreach (var a in _arrows.OrderBy(x => x.id))
            {
                sb.Append(a.cleared ? '1' : '0').Append(a.dir).Append('|');
                foreach (var c in a.cells) sb.Append(c.x).Append(',').Append(c.y).Append(';');
                sb.Append('#');
            }
            return sb.ToString();
        }

        static (int dc, int dr) Delta(string dir) => dir switch
        {
            "R" => (1, 0),
            "L" => (-1, 0),
            "D" => (0, 1),
            "U" => (0, -1),
            _ => (0, 0)
        };
    }
}
