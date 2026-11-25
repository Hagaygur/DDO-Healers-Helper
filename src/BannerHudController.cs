using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace VoK.HealersHelper;

internal enum HudCommand
{
    Preview,
    Move,
    MoveStatus,
    Stop
}

internal sealed class BannerHudController : IDisposable
{
    private readonly ConcurrentQueue<HudCommand> _commands = new();
    private readonly Thread _thread;
    private readonly IHealerEngineView _engine;
    private int _disposed;
    private string _status = "Starting DDO Healer's Helper HUD…";

    internal string Status => Volatile.Read(ref _status);

    public BannerHudController(IHealerEngineView engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _thread = new Thread(Run) { IsBackground = true, Name = "DDO Healer's Helper HUD" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    private void Run()
    {
        try
        {
            using var banner = new BannerHudWindow(_engine, _commands, s => Volatile.Write(ref _status, s));
            using var statusHud = new StatusHudWindow(_engine);
            using var tick = new System.Windows.Forms.Timer { Interval = 85 };

            tick.Tick += (_, _) =>
            {
                double now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
                while (_commands.TryDequeue(out var command))
                {
                    if (command == HudCommand.Stop)
                    {
                        tick.Stop();
                        banner.Hide();
                        statusHud.Hide();
                        Application.ExitThread();
                        return;
                    }

                    if (command == HudCommand.Preview)
                    {
                        banner.BeginPreview();
                    }
                    else if (command == HudCommand.Move)
                    {
                        banner.BeginMove();
                    }
                    else if (command == HudCommand.MoveStatus)
                    {
                        statusHud.BeginMove();
                    }
                }

                banner.TickFrame(now);
                statusHud.TickFrame(now);
            };

            tick.Start();
            Application.Run();
        }
        catch (Exception e)
        {
            Volatile.Write(ref _status, "HUD error: " + e.Message);
            _engine.Report("HUD failed: " + e);
        }
    }

    internal void Preview()
    {
        if (_disposed == 0)
        {
            _commands.Enqueue(HudCommand.Preview);
        }
    }

    internal void Move()
    {
        if (_disposed == 0)
        {
            _commands.Enqueue(HudCommand.Move);
        }
    }

    internal void MoveStatus()
    {
        if (_disposed == 0)
        {
            _commands.Enqueue(HudCommand.MoveStatus);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _commands.Enqueue(HudCommand.Stop);
        // Non-blocking teardown: the HUD STA thread drains Stop on its next ~85ms tick.
    }
}
