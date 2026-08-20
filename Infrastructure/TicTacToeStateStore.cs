using System.Collections.Concurrent;

namespace SpeedSaga.API.Infrastructure;

public sealed class TicTacToeStateStore
{
    readonly ConcurrentDictionary<string, TttSession> _sessions = new();

    public TttSession GetOrCreate(string sessionId, bool vsAi = false)
    {
        return _sessions.GetOrAdd(sessionId, _ => new TttSession(vsAi));
    }

    public void Remove(string sessionId) => _sessions.TryRemove(sessionId, out _);

    public TttMoveResult TryMove(string sessionId, Guid playerId, Guid player1Id, Guid? player2Id, int cellIndex, bool vsAi)
    {
        if (cellIndex < 0 || cellIndex > 8)
            return TttMoveResult.Fail("Invalid cell.");

        var session = GetOrCreate(sessionId, vsAi);
        lock (session)
        {
            if (session.Finished)
                return TttMoveResult.Fail("Game already finished.");

            int marker = ResolveMarker(playerId, player1Id, player2Id, vsAi);
            if (marker == 0)
                return TttMoveResult.Fail("Not a player in this session.");
            if (session.CurrentTurn != marker)
                return TttMoveResult.Fail("Not your turn.");
            if (session.Board[cellIndex] != 0)
                return TttMoveResult.Fail("Cell already taken.");

            session.Board[cellIndex] = marker;
            session.MoveCount++;
            session.EvaluateOutcome();

            if (!session.Finished && vsAi && session.CurrentTurn == 2)
                session.PlayAiMove();

            return TttMoveResult.Ok(session);
        }
    }

    static int ResolveMarker(Guid playerId, Guid player1Id, Guid? player2Id, bool vsAi)
    {
        if (playerId == player1Id) return 1;
        if (player2Id.HasValue && playerId == player2Id.Value) return 2;
        if (vsAi && playerId == player1Id) return 1;
        return 0;
    }

    public static string EmptyBoardJson(bool vsAi = false) =>
        System.Text.Json.JsonSerializer.Serialize(new { board = new int[9], vsAi, currentTurn = 1 });
}

public sealed class TttSession
{
    public TttSession(bool vsAi) => VsAi = vsAi;

    public bool VsAi { get; }
    public int[] Board { get; } = new int[9];
    public int CurrentTurn { get; internal set; } = 1;
    public int MoveCount { get; internal set; }
    public bool Finished { get; internal set; }
    public int Winner { get; internal set; }
    public bool IsDraw { get; internal set; }

    public void EvaluateOutcome()
    {
        Winner = CheckWinner(Board);
        if (Winner != 0)
        {
            Finished = true;
            return;
        }
        if (MoveCount >= 9)
        {
            Finished = true;
            IsDraw = true;
            return;
        }
        CurrentTurn = CurrentTurn == 1 ? 2 : 1;
    }

    public void PlayAiMove()
    {
        if (Finished || CurrentTurn != 2) return;
        int best = FindBestMove(Board, 2);
        if (best < 0) return;
        Board[best] = 2;
        MoveCount++;
        EvaluateOutcome();
    }

    static int CheckWinner(int[] b)
    {
        int[][] lines =
        {
            new[] {0,1,2}, new[] {3,4,5}, new[] {6,7,8},
            new[] {0,3,6}, new[] {1,4,7}, new[] {2,5,8},
            new[] {0,4,8}, new[] {2,4,6}
        };
        foreach (var line in lines)
        {
            int a = b[line[0]], c = b[line[1]], d = b[line[2]];
            if (a != 0 && a == c && c == d) return a;
        }
        return 0;
    }

    static int FindBestMove(int[] board, int player)
    {
        int bestScore = int.MinValue;
        int bestMove = -1;
        for (int i = 0; i < 9; i++)
        {
            if (board[i] != 0) continue;
            board[i] = player;
            int score = Minimax(board, player == 1 ? 2 : 1, player, 0);
            board[i] = 0;
            if (score > bestScore)
            {
                bestScore = score;
                bestMove = i;
            }
        }
        return bestMove;
    }

    static int Minimax(int[] board, int current, int aiPlayer, int depth)
    {
        int w = CheckWinner(board);
        if (w == aiPlayer) return 10 - depth;
        if (w != 0) return depth - 10;
        if (board.All(c => c != 0)) return 0;

        bool maximizing = current == aiPlayer;
        int best = maximizing ? int.MinValue : int.MaxValue;
        for (int i = 0; i < 9; i++)
        {
            if (board[i] != 0) continue;
            board[i] = current;
            int score = Minimax(board, current == 1 ? 2 : 1, aiPlayer, depth + 1);
            board[i] = 0;
            best = maximizing ? Math.Max(best, score) : Math.Min(best, score);
        }
        return best;
    }
}

public sealed class TttMoveResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public int[] Board { get; init; } = Array.Empty<int>();
    public int CurrentTurn { get; init; }
    public int Winner { get; init; }
    public bool IsDraw { get; init; }
    public bool Finished { get; init; }

    public static TttMoveResult Fail(string msg) => new() { Success = false, Message = msg };
    public static TttMoveResult Ok(TttSession s) => new()
    {
        Success = true,
        Message = "Move accepted",
        Board = (int[])s.Board.Clone(),
        CurrentTurn = s.CurrentTurn,
        Winner = s.Winner,
        IsDraw = s.IsDraw,
        Finished = s.Finished
    };
}
