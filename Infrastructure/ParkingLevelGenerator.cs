using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpeedSaga.API.Infrastructure;

public static class ParkingLevelGenerator
{
    const int Width = 6;
    const int Height = 6;
    const int ExitRow = 2;

    public static string Generate(string tier = "Easy")
    {
        var rng = Random.Shared;
        var cars = BuildTemplate();
        int shuffleMoves = tier switch
        {
            "Medium" => 18,
            "Hard" => 30,
            "SuperHard" => 45,
            _ => 10
        };

        var state = new ParkingState(cars);
        for (int i = 0; i < shuffleMoves; i++)
        {
            var movable = state.GetMovableCars(rng);
            if (movable.Count == 0) break;
            var pick = movable[rng.Next(movable.Count)];
            state.TryMove(pick.carId, pick.delta);
        }

        var layout = new ParkingLayoutDto
        {
            Width = Width,
            Height = Height,
            ExitRow = ExitRow,
            Tier = tier,
            Cars = state.ExportCars()
        };
        return JsonSerializer.Serialize(layout, JsonOptions);
    }

    static List<ParkingCarDef> BuildTemplate()
    {
        return new List<ParkingCarDef>
        {
            new(0, ExitRow, 1, 2, true, true),
            new(1, ExitRow, 3, 2, true, false),
            new(2, 0, 2, 2, false, false),
            new(3, 1, 0, 3, true, false),
            new(4, 3, 0, 2, false, false),
            new(5, 3, 3, 2, false, false),
            new(6, 4, 1, 3, true, false),
            new(7, 5, 0, 2, true, false),
            new(8, 5, 3, 2, true, false)
        };
    }

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    sealed class ParkingCarDef
    {
        public ParkingCarDef(int id, int row, int col, int len, bool horizontal, bool isTarget)
        {
            Id = id; Row = row; Col = col; Len = len; Horizontal = horizontal; IsTarget = isTarget;
        }
        public int Id { get; }
        public int Row { get; set; }
        public int Col { get; set; }
        public int Len { get; }
        public bool Horizontal { get; }
        public bool IsTarget { get; }
    }

    sealed class ParkingState
    {
        readonly Dictionary<int, ParkingCarDef> _cars;
        readonly int[,] _grid;

        public ParkingState(List<ParkingCarDef> cars)
        {
            _cars = cars.ToDictionary(c => c.Id);
            _grid = new int[Height, Width];
            RebuildGrid();
        }

        void RebuildGrid()
        {
            Array.Clear(_grid, 0, _grid.Length);
            foreach (var c in _cars.Values)
            {
                if (c.Horizontal)
                {
                    for (int i = 0; i < c.Len; i++)
                        _grid[c.Row, c.Col + i] = c.Id + 1;
                }
                else
                {
                    for (int i = 0; i < c.Len; i++)
                        _grid[c.Row + i, c.Col] = c.Id + 1;
                }
            }
        }

        public bool TryMove(int carId, int delta)
        {
            if (!_cars.TryGetValue(carId, out var car)) return false;
            int nr = car.Row, nc = car.Col;
            if (car.Horizontal) nc += delta;
            else nr += delta;
            if (!CanPlace(car, nr, nc)) return false;
            ClearCar(car);
            car.Row = nr;
            car.Col = nc;
            RebuildGrid();
            return true;
        }

        void ClearCar(ParkingCarDef car)
        {
            if (car.Horizontal)
            {
                for (int i = 0; i < car.Len; i++)
                    _grid[car.Row, car.Col + i] = 0;
            }
            else
            {
                for (int i = 0; i < car.Len; i++)
                    _grid[car.Row + i, car.Col] = 0;
            }
        }

        bool CanPlace(ParkingCarDef car, int row, int col)
        {
            if (car.Horizontal)
            {
                if (row < 0 || row >= Height || col < 0 || col + car.Len > Width) return false;
                for (int i = 0; i < car.Len; i++)
                {
                    int v = _grid[row, col + i];
                    if (v != 0 && v != car.Id + 1) return false;
                }
            }
            else
            {
                if (col < 0 || col >= Width || row < 0 || row + car.Len > Height) return false;
                for (int i = 0; i < car.Len; i++)
                {
                    int v = _grid[row + i, col];
                    if (v != 0 && v != car.Id + 1) return false;
                }
            }
            return true;
        }

        public List<(int carId, int delta)> GetMovableCars(Random rng)
        {
            var list = new List<(int, int)>();
            foreach (var car in _cars.Values)
            {
                if (TryMoveDry(car, -1)) list.Add((car.Id, -1));
                if (TryMoveDry(car, 1)) list.Add((car.Id, 1));
            }
            return list;
        }

        bool TryMoveDry(ParkingCarDef car, int delta)
        {
            int nr = car.Row, nc = car.Col;
            if (car.Horizontal) nc += delta;
            else nr += delta;
            return CanPlace(car, nr, nc);
        }

        public List<ParkingCarDto> ExportCars() =>
            _cars.Values.Select(c => new ParkingCarDto
            {
                Id = c.Id,
                Row = c.Row,
                Col = c.Col,
                Len = c.Len,
                Horizontal = c.Horizontal,
                IsTarget = c.IsTarget
            }).ToList();
    }

    public sealed class ParkingLayoutDto
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public int ExitRow { get; set; }
        public string Tier { get; set; } = "Easy";
        public List<ParkingCarDto> Cars { get; set; } = new();
    }

    public sealed class ParkingCarDto
    {
        public int Id { get; set; }
        public int Row { get; set; }
        public int Col { get; set; }
        public int Len { get; set; }
        public bool Horizontal { get; set; }
        public bool IsTarget { get; set; }
    }
}
