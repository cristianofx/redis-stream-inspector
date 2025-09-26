using Avalonia.Controls;
using Avalonia.Threading;
using RedisInspector.UI.Models;
using RedisInspector.UI.Services;
using RedisInspector.UI.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace RedisInspector.UI.Views;

public partial class MainWindow : Window, IWindowDialogService
{
    private List<Match> _matches = new();
    private int _currentMatchIndex = -1;

    public MainWindow()
    {
        InitializeComponent();

        // If VM already set, wire now
        if (DataContext is MainWindowViewModel vmNow)
            WireViewModel(vmNow);

        // Wire on future DataContext changes
        DataContextChanged += (_, __) =>
        {
            if (DataContext is MainWindowViewModel vm)
                WireViewModel(vm);
        };
    }

    private void WireViewModel(MainWindowViewModel vm)
    {
        // Dialog service
        vm.Dialogs = new WindowDialogService(this);

        // Unsubscribe first (in case of rewire)
        vm.SearchTextRequested -= OnSearchRequested;
        vm.SearchClearRequested -= OnSearchClear;
        vm.FocusFindRequested -= OnFocusFind;
        vm.PropertyChanged -= OnVmPropertyChanged;

        // Subscribe
        vm.SearchTextRequested += OnSearchRequested;
        vm.SearchClearRequested += OnSearchClear;
        vm.FocusFindRequested += OnFocusFind;
        vm.PropertyChanged += OnVmPropertyChanged;
    }

    // --- Dialog API (IWindowDialogService) ---

    public async Task<ConnectionProfile?> ShowEditConnectionAsync(ConnectionProfile? existing)
    {
        var decrypted = (DataContext as MainWindowViewModel)?.GetDecryptedPasswordFor(existing);
        var dlg = new EditConnectionWindow(existing) { Icon = this.Icon };
        return await dlg.ShowAsync(existing, decrypted);
    }

    public async Task<bool> ShowConfirmAsync(string title, string message)
    {
        var dlg = new ConfirmDialogWindow(title, message) { Icon = this.Icon };
        return await dlg.ShowDialog<bool>(this);
    }

    // --- Find support ---

    private void OnFocusFind()
    {
        // clear previous matches when opening the bar
        OnSearchClear();

        // focus query box (named x:Name="FindBox" in XAML)
        FindBox?.Focus();
        FindBox?.SelectAll();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // When inputs or source text change, clear cached matches so next search rebuilds
        if (e.PropertyName is nameof(MainWindowViewModel.SearchQuery)
                          or nameof(MainWindowViewModel.MatchCase)
                          or nameof(MainWindowViewModel.WholeWord)
                          or nameof(MainWindowViewModel.SelectedRawMessage))
        {
            OnSearchClear();
        }
    }

    private void OnSearchClear()
    {
        _matches.Clear();
        _currentMatchIndex = -1;

        if (RawBox is { })
            RawBox.SelectionStart = RawBox.SelectionEnd = RawBox.CaretIndex;

        if (DataContext is MainWindowViewModel vm)
            vm.SearchStatus = "";
    }

    private void OnSearchRequested(MainWindowViewModel.SearchTextDirection dir)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (RawBox is null) return;

        var text = RawBox.Text ?? string.Empty;
        var query = vm.SearchQuery ?? string.Empty;
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(query))
        {
            vm.SearchStatus = "";
            return;
        }

        // Rebuild matches each request (fast enough for UI)
        _matches = BuildMatches(text, query, vm.MatchCase, vm.WholeWord);
        if (_matches.Count == 0) { vm.SearchStatus = "0/0"; return; }

        _currentMatchIndex = _currentMatchIndex < 0
            ? 0
            : dir == MainWindowViewModel.SearchTextDirection.Next
                ? (_currentMatchIndex + 1) % _matches.Count
                : (_currentMatchIndex - 1 + _matches.Count) % _matches.Count;

        var m = _matches[_currentMatchIndex];

        // show highlight reliably: focus then defer selection
        RawBox.Focus();
        Dispatcher.UIThread.Post(() =>
        {
            RawBox.CaretIndex = Math.Min(m.Index + m.Length, text.Length);
            RawBox.SelectionStart = m.Index;
            RawBox.SelectionEnd = m.Index + m.Length;

            vm.SearchStatus = $"{_currentMatchIndex + 1}/{_matches.Count}";
        }, DispatcherPriority.Background);
    }

    private static List<Match> BuildMatches(string text, string query, bool matchCase, bool wholeWord)
    {
        var pattern = Regex.Escape(query);
        if (wholeWord) pattern = $@"\b{pattern}\b";

        var options = RegexOptions.CultureInvariant;
        if (!matchCase) options |= RegexOptions.IgnoreCase;

        return Regex.Matches(text, pattern, options).Cast<Match>().ToList();
    }

    // (kept for completeness if you still reference these helpers elsewhere)
    private static int IndexOf(string text, string query, int start, StringComparison cmp, bool wholeWord)
    {
        int idx = text.IndexOf(query, start, cmp);
        while (idx >= 0 && wholeWord && !IsWholeWordAt(text, idx, query.Length))
        {
            int nextStart = idx + 1;
            if (nextStart > text.Length) return -1;
            idx = text.IndexOf(query, nextStart, cmp);
        }
        return idx;
    }

    private static bool IsWholeWordAt(string text, int start, int length)
    {
        bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_';
        bool leftOk = start == 0 || !IsWord(text[start - 1]);
        int end = start + length;
        bool rightOk = end >= text.Length || !IsWord(text[end]);
        return leftOk && rightOk;
    }
}
