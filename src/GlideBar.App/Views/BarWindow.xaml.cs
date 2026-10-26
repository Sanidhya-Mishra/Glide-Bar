using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using GlideBar.App.Interop;
using GlideBar.App.Services;
using GlideBar.App.ViewModels;
using GlideBar.Core.Abstractions;
using GlideBar.Core.Models;

namespace GlideBar.App.Views;

/// <summary>
/// The overlay shell window. Owns all OS-level interaction (tool-window styling, acrylic blur,
/// global hotkey, monitor-aware placement) and the interaction state machine (hover dwell/grace
/// reveal, drag, magnetic edge snap, horizontal/vertical morph). Content lives in the bound
/// <see cref="BarViewModel"/>; window/OS concerns stay here (standard WPF practice).
/// </summary>
public partial class BarWindow : Window
{
    // Interaction constants (design-spec; would become settings-bound later).
    private const double ShadowMargin = 30;    // must match RootGrid.Margin (room for the neon bloom)
    private const int DwellMs = 120;
    private const int GraceMs = 400;
    private const double DragThreshold = 4;
    private const double SnapDistance = 24;
    private const int MorphMs = 180;

    private readonly BarViewModel _vm;
    private readonly ISettingsService _settings;
    private readonly ThemeManager _theme;

    private readonly DispatcherTimer _dwell;
    private readonly DispatcherTimer _grace;

    private HotKeyService? _hotkey;
    private IntPtr _hwnd;

    private bool _mouseInside;
    private bool _dragPending;
    private bool _dragging;
    private NativeMethods.POINT _dragStartCursor;
    private double _dragStartLeft, _dragStartTop;

    // Where the bar hangs from (logical/DIU, virtual-screen space) + which edge it is anchored to.
    private Point _anchor;
    private BarEdge _edge = BarEdge.None;

    public BarWindow(BarViewModel vm, ISettingsService settings, ThemeManager theme)
    {
        _vm = vm;
        _settings = settings;
        _theme = theme;
        DataContext = vm;
        InitializeComponent();

        AddPopup.CustomPopupPlacementCallback = PlaceAddPopup;
        ControlCenterPopup.CustomPopupPlacementCallback = PlaceControlCenter;

        _dwell = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(DwellMs) };
        _dwell.Tick += (_, _) => { _dwell.Stop(); Expand(); };
        _grace = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(GraceMs) };
        _grace.Tick += (_, _) => { _grace.Stop(); TryCollapse(); };

        MouseEnter += OnMouseEnterWindow;
        MouseLeave += OnMouseLeaveWindow;
        Surface.MouseLeftButtonDown += OnSurfaceMouseDown;
        Surface.MouseMove += OnSurfaceMouseMove;
        Surface.MouseLeftButtonUp += OnSurfaceMouseUp;

        _theme.ThemeChanged += (_, _) => ApplyWindowChrome();

        // Drag a file/folder onto the bar to pin it; resize the window whenever the pin set changes.
        DragEnter += OnDragEnter;
        DragOver += OnDragOver;
        Drop += OnDrop;
        _vm.Pins.Pins.CollectionChanged += (_, _) => { if (_vm.IsExpanded) ApplyVisualState(animate: true); };

        // Resize the bar when the search field expands/collapses, and focus it on expand (horizontal).
        _vm.Search.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(SearchViewModel.IsFieldOpen)) return;
            if (_vm.IsExpanded) ApplyVisualState(animate: true);
            if (_vm.Search.IsFieldOpen && _vm.IsHorizontal)
                Dispatcher.BeginInvoke(DispatcherPriority.Input,
                    () => { HSearchBox.Focus(); Keyboard.Focus(HSearchBox); });
        };

        // Resize when the TEMP gauge is shown/hidden (toggled from the settings flyout).
        _vm.Vitals.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(VitalsViewModel.TempAvailable) && _vm.IsExpanded)
                ApplyVisualState(animate: true);
        };

        // Open/close the Control Center with a scale + fade transition.
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BarViewModel.IsControlCenterOpen))
                AnimateControlCenter(_vm.IsControlCenterOpen);
        };
    }

    // ---------------------------------------------------------------------------------------
    //  Control Center open/close transition (the popup content scales down from the bar + fades)
    // ---------------------------------------------------------------------------------------
    private void AnimateControlCenter(bool open)
    {
        if (open)
        {
            // Just show it — the Opened event plays the entrance so the first frame isn't dropped.
            ControlCenterPopup.IsOpen = true;
        }
        else
        {
            if (!ControlCenterPopup.IsOpen) return;

            if (_vm.ReduceEffects)
            {
                ControlCenterPopup.IsOpen = false;
                return;
            }

            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            var dur = TimeSpan.FromMilliseconds(140);
            var fadeOut = new DoubleAnimation(1, 0, dur) { EasingFunction = ease };
            fadeOut.Completed += (_, _) =>
            {
                ControlCenterPopup.IsOpen = false; // close only after the transition finishes
                CCScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                CCTranslate.BeginAnimation(TranslateTransform.YProperty, null);
                ControlCenterContent.BeginAnimation(OpacityProperty, null);
                CCScale.ScaleY = 1; CCTranslate.Y = 0; ControlCenterContent.Opacity = 1;
            };
            CCScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(1.0, 0.97, dur) { EasingFunction = ease });
            CCTranslate.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(0, -8, dur) { EasingFunction = ease });
            ControlCenterContent.BeginAnimation(OpacityProperty, fadeOut);
        }
    }

    // Play the entrance (scale + drop + fade) once the popup is actually shown.
    private void ControlCenterPopup_Opened(object? sender, EventArgs e)
    {
        if (_vm.ReduceEffects)
        {
            CCScale.ScaleY = 1; CCTranslate.Y = 0; ControlCenterContent.Opacity = 1;
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var dur = TimeSpan.FromMilliseconds(190);
        CCScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.95, 1.0, dur) { EasingFunction = ease });
        CCTranslate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(-10, 0, dur) { EasingFunction = ease });
        ControlCenterContent.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
    }

    // Edge-aware placement for the Control Center: centred on the side of the bar that faces the
    // screen interior — below for top/floating, above for bottom, right for a left-dock, left for a
    // right-dock. Coordinates target the VISIBLE panel border, correcting for its uniform margin.
    private const double CcMargin = 12; // must match ControlCenterPanel's Border Margin
    private const double CcGap = 3;     // clearance between the bar and the panel

    private CustomPopupPlacement[] PlaceControlCenter(Size popup, Size target, Point offset)
    {
        const double m = CcMargin, gap = CcGap;
        double x, y;
        PopupPrimaryAxis axis;
        switch (_edge)
        {
            case BarEdge.Left:  // bar on left  -> panel to the right, vertically centred
                x = target.Width + gap - m;
                y = (target.Height - popup.Height) / 2;
                axis = PopupPrimaryAxis.Vertical;
                break;
            case BarEdge.Right: // bar on right -> panel to the left, vertically centred
                x = -popup.Width + m - gap;
                y = (target.Height - popup.Height) / 2;
                axis = PopupPrimaryAxis.Vertical;
                break;
            case BarEdge.Bottom: // bar on bottom -> panel above, horizontally centred
                x = (target.Width - popup.Width) / 2;
                y = -popup.Height + m - gap;
                axis = PopupPrimaryAxis.Horizontal;
                break;
            default: // Top / floating -> panel below, horizontally centred
                x = (target.Width - popup.Width) / 2;
                y = target.Height + gap - m;
                axis = PopupPrimaryAxis.Horizontal;
                break;
        }
        return new[] { new CustomPopupPlacement(new Point(x, y), axis) };
    }

    // Re-run the placement callback when the bar is re-docked while the panel is open.
    private void UpdateControlCenterPlacement()
    {
        if (!ControlCenterPopup.IsOpen) return;
        double h = ControlCenterPopup.HorizontalOffset;
        ControlCenterPopup.HorizontalOffset = h + 1;
        ControlCenterPopup.HorizontalOffset = h;
    }

    // ---------------------------------------------------------------------------------------
    //  Window / OS init
    // ---------------------------------------------------------------------------------------
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;

        WindowEffects.MakeToolWindow(_hwnd); // hide from Alt+Tab / taskbar

        var src = HwndSource.FromHwnd(_hwnd);
        src?.AddHook(WndProc); // WM_DISPLAYCHANGE re-assert

        _hotkey = new HotKeyService(_hwnd);
        _hotkey.Pressed += (_, _) => Dispatcher.BeginInvoke(ToggleSummon);
        if (!_hotkey.Register(_settings.Settings.Hotkey))
            _hotkey.Register("Ctrl+Space"); // fall back to the default

        ApplyWindowChrome();
        InitPlacement();
        ApplyVisualState(animate: false); // start collapsed at the anchor
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_DISPLAYCHANGE)
        {
            // Re-assert topmost ONCE on display change (never per-frame) and re-validate placement.
            Topmost = false; Topmost = true;
            ValidatePlacement();
            ApplyVisualState(animate: false);
        }
        return IntPtr.Zero;
    }

    private void ApplyWindowChrome()
    {
        if (_hwnd == IntPtr.Zero) return;
        bool dark = _theme.Current == AppTheme.Dark;
        // Window-wide acrylic tints the ENTIRE window rect (including the transparent shadow margin),
        // showing as a dark box around the pill. Keep it OFF so only the pill + its glow are visible;
        // the translucent SurfaceBrush provides the glassy look instead.
        WindowEffects.SetBlurBehind(_hwnd, false, dark);

        // Reduce-effects => opaque surface (no translucency relying on blur behind).
        if (_vm.ReduceEffects) Surface.SetResourceReference(BackgroundProperty, "SurfaceSolidBrush");
        else Surface.SetResourceReference(BackgroundProperty, "SurfaceBrush");
    }

    // ---------------------------------------------------------------------------------------
    //  Placement (multi-monitor aware)
    // ---------------------------------------------------------------------------------------
    private void InitPlacement()
    {
        var p = _settings.Settings.Placement;
        _vm.Orientation = p.Orientation;
        _edge = p.Edge;

        bool hasSaved = !p.Floating && (p.X != 0 || p.Y != 0) || (p.Floating && (p.X != 0 || p.Y != 0));
        if (hasSaved && PointOnAnyMonitor(new Point(p.X, p.Y)))
        {
            _anchor = new Point(p.X, p.Y);
        }
        else
        {
            ResetToDefaultAnchor();
        }
    }

    private void ResetToDefaultAnchor()
    {
        var wa = SystemParameters.WorkArea; // primary, DIU
        // Float a little below the top edge so it's clearly visible on first run (not jammed into
        // the top edge / other apps' title bars). The user can drag it to any edge to dock it.
        _anchor = new Point(wa.Left + wa.Width / 2, wa.Top + 40);
        _edge = BarEdge.None;
        _vm.Orientation = BarOrientation.Horizontal;
    }

    private void ValidatePlacement()
    {
        if (!PointOnAnyMonitor(_anchor)) ResetToDefaultAnchor();
    }

    private bool PointOnAnyMonitor(Point logical)
    {
        foreach (var wa in LogicalWorkAreas())
            if (wa.Contains(logical)) return true;
        return false;
    }

    /// <summary>Monitor work areas converted to logical (DIU) units using the current DPI.</summary>
    private IEnumerable<Rect> LogicalWorkAreas()
    {
        var (sx, sy) = Dpi();
        foreach (var m in MonitorHelper.GetMonitors())
            yield return new Rect(m.WorkArea.X / sx, m.WorkArea.Y / sy, m.WorkArea.Width / sx, m.WorkArea.Height / sy);
    }

    private Rect WorkAreaFor(Point logical)
    {
        foreach (var wa in LogicalWorkAreas())
            if (wa.Contains(logical)) return wa;
        return SystemParameters.WorkArea;
    }

    private (double sx, double sy) Dpi()
    {
        var src = PresentationSource.FromVisual(this);
        if (src?.CompositionTarget is { } ct)
            return (ct.TransformToDevice.M11, ct.TransformToDevice.M22);
        return (1, 1);
    }

    // ---------------------------------------------------------------------------------------
    //  Visual state (size + position + morph)
    // ---------------------------------------------------------------------------------------
    // Defer sizing to Loaded priority so that pending visibility/binding updates (collapsed <->
    // expanded) and a layout pass are applied BEFORE we measure the surface. Measuring too early
    // sizes the window to the wrong (collapsed) content and clips the expanded bar.
    private void ApplyVisualState(bool animate)
        => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => ApplyVisualStateCore(animate)));

    private void ApplyVisualStateCore(bool animate)
    {
        bool vertical = _vm.Orientation == BarOrientation.Vertical;
        Size s;

        if (!_vm.IsExpanded)
        {
            // Collapsed = a thin, subtle "scroll-bar" handle: slim light thumb, gentle glow.
            const double hThick = 5;    // handle thickness
            const double hLen = 94;     // handle length
            const double padAlong = 7;  // padding along the length
            const double padCross = 2;  // padding across the thin dimension
            const double border = 0.8;

            Surface.Padding = vertical ? new Thickness(padCross, padAlong, padCross, padAlong)
                                       : new Thickness(padAlong, padCross, padAlong, padCross);
            Surface.CornerRadius = new CornerRadius((hThick + 2 * padCross) / 2 + border);
            Surface.BorderThickness = new Thickness(border);
            SurfaceShadow.BlurRadius = 9;
            SurfaceShadow.Opacity = 0.3;

            Handle.Width = vertical ? hThick : hLen;
            Handle.Height = vertical ? hLen : hThick;

            double along = hLen + 2 * padAlong + 2 * border;
            double cross = hThick + 2 * padCross + 2 * border;
            s = vertical ? new Size(cross, along) : new Size(along, cross);
        }
        else
        {
            // Expanded = full rounded bar with the glow.
            Surface.Padding = new Thickness(9);
            Surface.CornerRadius = vertical ? new CornerRadius(15) : new CornerRadius(26);
            Surface.BorderThickness = new Thickness(1.2);
            SurfaceShadow.BlurRadius = 18;
            SurfaceShadow.Opacity = 0.72;

            // Measure content UNCONSTRAINED. Do NOT UpdateLayout() before reading DesiredSize —
            // a constrained relayout overwrites it and clips the bar to the search box.
            Surface.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            s = Surface.DesiredSize;
            if (s.Width <= 0 || s.Height <= 0) s = new Size(80, 20);
        }

        double winW = s.Width + 2 * ShadowMargin;
        double winH = s.Height + 2 * ShadowMargin;
        var (left, top) = AnchorToWindow(winW, winH, s);

        // Resize/position the window in ONE step (no per-frame geometry animation — that forces a
        // full layout pass + HWND resize each frame on a software-rendered layered window = jank).
        SetBoundsImmediate(left, top, winW, winH);

        // Keep the add panel on the interior-facing side (and sized to the bar) if re-docked while open.
        if (AddPopup.IsOpen) { SizeAddPanel(); UpdateAddPlacement(); }
        if (ControlCenterPopup.IsOpen) UpdateControlCenterPlacement();

        // The visible motion is a GPU-composited scale + fade of the content, which is smooth.
        if (animate && !_vm.ReduceEffects)
            PlayRevealTransition();
    }

    private (double left, double top) AnchorToWindow(double winW, double winH, Size surface)
    {
        double left, top;
        const double M = ShadowMargin;
        switch (_edge)
        {
            case BarEdge.Bottom:
                left = _anchor.X - winW / 2; top = _anchor.Y - winH + M; break;
            case BarEdge.Left:
                left = _anchor.X - M; top = _anchor.Y - winH / 2; break;
            case BarEdge.Right:
                left = _anchor.X - winW + M; top = _anchor.Y - winH / 2; break;
            case BarEdge.Top:
            case BarEdge.None:
            default:
                left = _anchor.X - winW / 2; top = _anchor.Y - M; break;
        }

        // Keep the visible surface inside the owning monitor's work area.
        var wa = WorkAreaFor(_anchor);
        double surfLeft = Math.Clamp(left + M, wa.Left, Math.Max(wa.Left, wa.Right - surface.Width));
        double surfTop = Math.Clamp(top + M, wa.Top, Math.Max(wa.Top, wa.Bottom - surface.Height));
        return (surfLeft - M, surfTop - M);
    }

    private void SetBoundsImmediate(double left, double top, double w, double h)
    {
        ClearBoundsAnimations();
        Width = w; Height = h; Left = left; Top = top;
    }

    private void PlayRevealTransition()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var scale = new DoubleAnimation(0.90, 1.0, TimeSpan.FromMilliseconds(MorphMs)) { EasingFunction = ease };
        var fade = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease };
        SurfaceScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
        SurfaceScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
        Surface.BeginAnimation(OpacityProperty, fade);
    }

    private void ClearBoundsAnimations()
    {
        BeginAnimation(WidthProperty, null);
        BeginAnimation(HeightProperty, null);
        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);
    }

    // ---------------------------------------------------------------------------------------
    //  Expand / collapse
    // ---------------------------------------------------------------------------------------
    private void Expand()
    {
        if (_vm.IsExpanded) return;
        _vm.SetExpanded(true);
        ApplyVisualState(animate: true);
    }

    private void TryCollapse()
    {
        if (_vm.IsPinnedOpen || _mouseInside || _dragging || SearchActive) return;
        _vm.SetExpanded(false);
        ApplyVisualState(animate: true);
    }

    private bool SearchActive =>
        !string.IsNullOrWhiteSpace(_vm.Search.Query) || _vm.Search.HasResults || _vm.IsSettingsOpen
        || _vm.IsAddOpen || _vm.IsControlCenterOpen || _vm.Search.IsFieldOpen;

    public void Summon()
    {
        _vm.IsPinnedOpen = true;
        if (!_vm.IsExpanded) { _vm.SetExpanded(true); ApplyVisualState(animate: true); }
        Activate();
        if (_vm.IsHorizontal)
            _vm.Search.IsFieldOpen = true; // expands the field; the PropertyChanged hook focuses it
    }

    public void Dismiss()
    {
        _vm.IsPinnedOpen = false;
        _vm.SetExpanded(false);
        ApplyVisualState(animate: true);
    }

    public void ToggleSummon()
    {
        if (_vm.IsExpanded) Dismiss();
        else Summon();
    }

    // ---------------------------------------------------------------------------------------
    //  Hover
    // ---------------------------------------------------------------------------------------
    private void OnMouseEnterWindow(object sender, MouseEventArgs e)
    {
        _mouseInside = true;
        _grace.Stop();
        if (!_vm.IsExpanded && !_dragging) _dwell.Start();
    }

    private void OnMouseLeaveWindow(object sender, MouseEventArgs e)
    {
        _mouseInside = false;
        _dwell.Stop();
        if (!_vm.IsPinnedOpen && !_dragging) _grace.Start();
    }

    // ---------------------------------------------------------------------------------------
    //  Drag + magnetic snap
    // ---------------------------------------------------------------------------------------
    private void OnSurfaceMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (IsInteractive(e.OriginalSource as DependencyObject)) return;

        NativeMethods.GetCursorPos(out _dragStartCursor);
        _dragStartLeft = Left;
        _dragStartTop = Top;
        _dragPending = true;
        _dragging = false;
        Surface.CaptureMouse();
    }

    private void OnSurfaceMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragPending) return;
        NativeMethods.GetCursorPos(out var cur);
        var (sx, sy) = Dpi();
        double dx = (cur.X - _dragStartCursor.X) / sx;
        double dy = (cur.Y - _dragStartCursor.Y) / sy;

        if (!_dragging)
        {
            if (Math.Abs(dx) < DragThreshold && Math.Abs(dy) < DragThreshold) return;
            BeginDrag();
        }

        ClearBoundsAnimations();
        Left = _dragStartLeft + dx;
        Top = _dragStartTop + dy;
    }

    private void OnSurfaceMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragPending) Surface.ReleaseMouseCapture();

        if (_dragging)
        {
            _dragging = false;
            _vm.IsDragging = false;
            SurfaceShadow.BlurRadius = 18;
            FinalizeSnap();
        }
        else if (!_vm.IsExpanded)
        {
            // A click on the collapsed handle opens + pins the bar.
            Summon();
        }
        _dragPending = false;
    }

    private void BeginDrag()
    {
        _dragging = true;
        _vm.IsDragging = true;
        _dwell.Stop();
        _grace.Stop();
        SurfaceShadow.BlurRadius = 23; // glow grows while dragging
    }

    private void FinalizeSnap()
    {
        // Current visible surface rectangle (DIU).
        double surfLeft = Left + ShadowMargin;
        double surfTop = Top + ShadowMargin;
        double surfW = ActualWidth - 2 * ShadowMargin;
        double surfH = ActualHeight - 2 * ShadowMargin;
        var center = new Point(surfLeft + surfW / 2, surfTop + surfH / 2);

        var wa = WorkAreaFor(center);
        double dLeft = surfLeft - wa.Left;
        double dRight = wa.Right - (surfLeft + surfW);
        double dTop = surfTop - wa.Top;
        double dBottom = wa.Bottom - (surfTop + surfH);
        double min = Math.Min(Math.Min(dLeft, dRight), Math.Min(dTop, dBottom));

        if (min <= SnapDistance)
        {
            if (min == dLeft) { _edge = BarEdge.Left; _vm.Orientation = BarOrientation.Vertical; _anchor = new Point(wa.Left, center.Y); }
            else if (min == dRight) { _edge = BarEdge.Right; _vm.Orientation = BarOrientation.Vertical; _anchor = new Point(wa.Right, center.Y); }
            else if (min == dTop) { _edge = BarEdge.Top; _vm.Orientation = BarOrientation.Horizontal; _anchor = new Point(center.X, wa.Top); }
            else { _edge = BarEdge.Bottom; _vm.Orientation = BarOrientation.Horizontal; _anchor = new Point(center.X, wa.Bottom); }
        }
        else
        {
            _edge = BarEdge.None;
            _vm.Orientation = BarOrientation.Horizontal;
            _anchor = new Point(center.X, surfTop);
        }

        ApplyVisualState(animate: true);
        PersistPlacement();
    }

    private void PersistPlacement()
    {
        var pl = _settings.Settings.Placement;
        _settings.Settings.Placement = new BarPlacement
        {
            MonitorIndex = pl.MonitorIndex,
            Edge = _edge,
            Floating = _edge == BarEdge.None,
            Orientation = _vm.Orientation,
            X = _anchor.X,
            Y = _anchor.Y,
        };
        _ = _settings.SaveAsync();
    }

    /// <summary>True if the hit element is (inside) an interactive control, so dragging is suppressed.</summary>
    private static bool IsInteractive(DependencyObject? d)
    {
        while (d is not null)
        {
            if (d is ButtonBase or TextBoxBase or Thumb or ListBoxItem) return true;
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : null;
        }
        return false;
    }

    // ---------------------------------------------------------------------------------------
    //  Drag-and-drop to pin
    // ---------------------------------------------------------------------------------------
    private void OnDragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) && !_vm.IsExpanded) Expand();
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        _vm.IsPinnedOpen = true;
        if (!_vm.IsExpanded) _vm.SetExpanded(true);
        foreach (var p in paths) _vm.Pins.AddPath(p);
    }

    // Collapse the horizontal search field back to its icon when it's left empty.
    private void HSearchBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_vm.Search.Query))
            _vm.Search.IsFieldOpen = false;
    }

    private void AddPopup_Opened(object sender, EventArgs e)
    {
        SizeAddPanel();
        UpdateAddPlacement();
    }

    // Add-panel grid geometry.
    private const double TileExtent = 94;    // AppTileButton width(88) + 3px margin each side
    private const double PanelPadding = 28;  // Border Padding(14) * 2
    private const double ScrollReserve = 12; // room kept for the vertical scrollbar

    // Size the panel to a WHOLE number of tile columns that fits inside the bar's width, so the grid
    // is flush (no ragged right gutter) and the panel never exceeds the bar above. Docked horizontally
    // it grows with the bar; docked vertically (thin pill) it keeps a compact 3-column width.
    private void SizeAddPanel()
    {
        bool horizontal = _edge is BarEdge.Top or BarEdge.Bottom or BarEdge.None;

        int cols;
        if (horizontal)
        {
            double avail = Surface.ActualWidth - PanelPadding - ScrollReserve;
            cols = Math.Max(3, (int)Math.Floor(avail / TileExtent));
        }
        else
        {
            cols = 3;
        }

        AddPanel.Width = cols * TileExtent + PanelPadding + ScrollReserve;
    }

    private const double PanelMargin = 16; // must match AddPanel.Margin in XAML
    private const double PanelGap = 8;     // clearance between the bar and the panel

    // Centre the add panel on the side of the bar that faces the screen interior (below when docked
    // top/floating, above when bottom, right when docked left, left when docked right).
    //
    // NOTE: the `popup` size here EXCLUDES the Border's margin, but WPF renders the Border offset by
    // that margin inside the popup window. So every coordinate below is corrected by PanelMargin to
    // place the VISIBLE panel (not the transparent margin) adjacent to the bar — otherwise the margin
    // overlaps the bar.
    private CustomPopupPlacement[] PlaceAddPopup(Size popup, Size target, Point offset)
    {
        const double m = PanelMargin, gap = PanelGap;
        double x, y;
        PopupPrimaryAxis axis;
        switch (_edge)
        {
            case BarEdge.Left:  // bar on left  -> panel to the right, vertically centred
                x = target.Width + gap - m;
                y = target.Height / 2 - popup.Height / 2 - m;
                axis = PopupPrimaryAxis.Vertical;
                break;
            case BarEdge.Right: // bar on right -> panel to the left, vertically centred
                x = -popup.Width - gap - m;
                y = target.Height / 2 - popup.Height / 2 - m;
                axis = PopupPrimaryAxis.Vertical;
                break;
            case BarEdge.Bottom: // bar on bottom -> panel above, horizontally centred
                x = target.Width / 2 - popup.Width / 2 - m;
                y = -popup.Height - gap - m;
                axis = PopupPrimaryAxis.Horizontal;
                break;
            default: // Top / floating -> panel below, horizontally centred
                x = target.Width / 2 - popup.Width / 2 - m;
                y = target.Height + gap - m;
                axis = PopupPrimaryAxis.Horizontal;
                break;
        }
        return new[] { new CustomPopupPlacement(new Point(x, y), axis) };
    }

    // Re-run the placement callback when the bar is re-docked while the panel is open.
    private void UpdateAddPlacement()
    {
        if (!AddPopup.IsOpen) return;
        double h = AddPopup.HorizontalOffset;
        AddPopup.HorizontalOffset = h + 1;
        AddPopup.HorizontalOffset = h;
    }

    // Focus the vertical-dock search field as soon as its flyout opens.
    private void VSearchPopup_Opened(object sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            Activate();
            VSearchBox.Focus();
            Keyboard.Focus(VSearchBox);
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        _hotkey?.Dispose();
        base.OnClosed(e);
    }
}
