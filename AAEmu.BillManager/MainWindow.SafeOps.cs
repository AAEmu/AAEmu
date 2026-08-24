using System.Diagnostics;
using System.Text.Json;
using System.Windows;

namespace AAEmu.BillManager;

public partial class MainWindow
{
    private readonly BillAdminClient _adminClient = new();
    private bool _uiBusy;
    private bool _forceClose;

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_forceClose)
            return;

        if (HasUnsavedChanges())
        {
            var choice = MessageBox.Show(
                this,
                "You have unsaved catalog edits. Save all before closing?",
                "Unsaved changes",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);

            if (choice == MessageBoxResult.Cancel)
            {
                e.Cancel = true;
                return;
            }

            if (choice == MessageBoxResult.Yes)
            {
                e.Cancel = true;
                _ = SaveAllDirtyAsync(showConfirm: false).ContinueWith(t =>
                {
                    if (t.IsFaulted || t.Result == false)
                        return;
                    Dispatcher.Invoke(() =>
                    {
                        _forceClose = true;
                        Close();
                    });
                }, TaskScheduler.Default);
                return;
            }
        }

        if (_managedBill is { HasExited: false })
        {
            var stop = MessageBox.Show(
                this,
                "Bill Server is still running under this window. Stop it gracefully before closing?",
                "Bill Server running",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (stop == MessageBoxResult.Cancel)
            {
                e.Cancel = true;
                return;
            }

            if (stop == MessageBoxResult.Yes)
            {
                e.Cancel = true;
                _ = StopBillSafelyAsync(managedOnly: true, killExternal: false).ContinueWith(_ =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        _forceClose = true;
                        Close();
                    });
                }, TaskScheduler.Default);
            }
        }
    }

    private async void OnSaveAll(object sender, RoutedEventArgs e) => await SaveAllDirtyAsync(showConfirm: true);

    private async void OnStopBill(object sender, RoutedEventArgs e) =>
        await StopBillSafelyAsync(managedOnly: false, killExternal: true);

    private async void OnRestartBill(object sender, RoutedEventArgs e)
    {
        await StopBillSafelyAsync(managedOnly: false, killExternal: true);
        await Task.Delay(1500);
        OnStartBill(sender, e);
    }

    private async void OnPublish(object sender, RoutedEventArgs e)
    {
        if (HasUnsavedChanges())
        {
            var saveFirst = MessageBox.Show(
                this,
                "Save all catalog edits to Bill before publishing to the in-game shop database?",
                "Publish to ICS",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (saveFirst == MessageBoxResult.Cancel)
                return;

            if (saveFirst == MessageBoxResult.Yes)
            {
                if (!await SaveAllDirtyAsync(showConfirm: false))
                    return;
            }
        }

        var confirm = MessageBox.Show(
            this,
            "Publish available products to aaemu_game ics_* tables?\n\nAfter publish, run in-game:\n/ics off → /ics reload → /ics on",
            "Publish to ICS",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Information);

        if (confirm != MessageBoxResult.OK)
            return;

        await RunAdminOpAsync("Publishing to ICS…", async ct =>
        {
            var (ok, body, code) = await _adminClient.PublishAsync(AdminBase, ct);
            if (!ok)
                throw new InvalidOperationException($"Publish failed ({code}): {body}");
            return "Publish ICS: " + body;
        });
    }

    private bool HasUnsavedChanges() => _allRows.Any(r => r.IsDirty);

    private IEnumerable<ProductRow> GetDirtyRows() => _allRows.Where(r => r.IsDirty).ToList();

    private async Task<bool> SaveAllDirtyAsync(bool showConfirm)
    {
        var dirty = GetDirtyRows().ToList();
        if (dirty.Count == 0)
        {
            if (showConfirm)
                StatusLine.Text = "Nothing to save.";
            return true;
        }

        if (showConfirm)
        {
            var confirm = MessageBox.Show(
                this,
                $"Save {dirty.Count} changed product(s) to Bill catalog?",
                "Save all",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.OK)
                return false;
        }

        var payload = dirty.Select(ToSavePayload).ToList();
        var ok = await RunAdminOpAsync($"Saving {dirty.Count} product(s)…", async ct =>
        {
            var (success, body, code) = await _adminClient.BulkSaveAsync(AdminBase, payload, ct);
            if (!success && code != 207)
                throw new InvalidOperationException($"Save all failed ({code}): {body}");
            return $"Saved {dirty.Count} row(s): {body}";
        });

        if (ok)
        {
            foreach (var row in dirty)
                row.IsDirty = false;
            await RefreshCatalogAsync();
        }

        return ok;
    }

    private static object ToSavePayload(ProductRow row) => new
    {
        shopId = row.ShopId,
        row.Name,
        row.Available,
        row.Price,
        row.DiscountPrice,
        row.BuyLimit,
        row.LimitType,
        row.ItemId,
        row.MainTab,
        row.SubTab,
        row.TabPos
    };

    private async Task StopBillSafelyAsync(bool managedOnly, bool killExternal)
    {
        if (HasUnsavedChanges())
        {
            var choice = MessageBox.Show(
                this,
                "Unsaved catalog edits will be lost if Bill stops now. Save all first?",
                "Stop Bill Server",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);

            if (choice == MessageBoxResult.Cancel)
                return;

            if (choice == MessageBoxResult.Yes && !await SaveAllDirtyAsync(showConfirm: false))
                return;
        }

        await RunAdminOpAsync("Stopping Bill Server…", async ct =>
        {
            if (await _adminClient.GetStatusAsync(AdminBase, ct) is not null)
            {
                await _adminClient.RequestShutdownAsync(AdminBase, ct);
                await _adminClient.WaitForOfflineAsync(AdminBase, TimeSpan.FromSeconds(8), ct);
            }

            StopBillProcesses(managedOnly: managedOnly, killExternal: killExternal);
            return "Bill Server stopped.";
        }, disableServerControls: true);
    }

    private void StopBillProcesses(bool managedOnly, bool killExternal)
    {
        try
        {
            var stopped = 0;

            if (_managedBill is { HasExited: false })
            {
                try
                {
                    if (!_managedBill.CloseMainWindow())
                        _managedBill.Kill(entireProcessTree: true);
                    _managedBill.WaitForExit(5000);
                }
                catch
                {
                    try { _managedBill.Kill(entireProcessTree: true); } catch { /* ignore */ }
                }

                _managedBill.Dispose();
                _managedBill = null;
                stopped++;
            }

            if (!managedOnly && killExternal)
            {
                foreach (var p in Process.GetProcessesByName("AAEmu.BillServer"))
                {
                    try
                    {
                        if (p.HasExited)
                            continue;
                        p.Kill(entireProcessTree: true);
                        stopped++;
                    }
                    catch
                    {
                        // ignore per-process failures
                    }
                    finally
                    {
                        p.Dispose();
                    }
                }
            }

            StatusLine.Text = stopped > 0
                ? $"Stopped Bill Server ({stopped} process(es))."
                : "Bill Server was not running.";
            UpdateStartStopButtons();
            _ = PollServerStateAsync();
        }
        catch (Exception ex)
        {
            StatusLine.Text = "Stop failed: " + ex.Message;
        }
    }

    private async Task<bool> RunAdminOpAsync(
        string busyMessage,
        Func<CancellationToken, Task<string>> action,
        bool disableServerControls = false)
    {
        if (_uiBusy)
        {
            StatusLine.Text = "Another operation is still running.";
            return false;
        }

        SetUiBusy(true, busyMessage, disableServerControls);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            StatusLine.Text = await action(cts.Token);
            return true;
        }
        catch (Exception ex)
        {
            StatusLine.Text = ex.Message;
            return false;
        }
        finally
        {
            SetUiBusy(false, null, disableServerControls);
            await PollServerStateAsync();
        }
    }

    private void SetUiBusy(bool busy, string? message, bool includeServerControls = false)
    {
        _uiBusy = busy;
        if (!string.IsNullOrWhiteSpace(message))
            StatusLine.Text = message;

        Grid.IsEnabled = !busy;
        SaveAllButton.IsEnabled = !busy;
        if (includeServerControls)
        {
            StartBillButton.IsEnabled = !busy;
            StopBillButton.IsEnabled = !busy;
            RestartBillButton.IsEnabled = !busy;
        }
        else if (!busy)
        {
            UpdateStartStopButtons();
        }
    }

    private async Task RefreshCatalogAsync()
    {
        var json = await _adminClient.GetCatalogJsonAsync(AdminBase);
        _allRows = JsonSerializer.Deserialize<List<ProductRow>>(json, JsonOpts) ?? [];
        foreach (var row in _allRows)
            row.IsDirty = false;
        ApplyDisplayNames();
        ApplyFilter();
    }
}
