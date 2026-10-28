// Desktop Organizer - main window (appended after Core.cs at build time; shares its using block)
namespace DesktopOrganizer
{
    public class Home
    {
        Window win;
        TextBox Q; TextBlock Ph, Prompt, Hint, Clock, DateText, Greeting, Status, Caption, HotkeyText;
        ListBox Cats, L;
        Grid HomeView, ResultsView;
        Border Ans, AnsPill; TextBlock AnsVerdict, AnsText, AnsDetail; WrapPanel AnsLinks; Answer lastAns;
        StackPanel Shelves;
        Image Bg, PvImage;
        Border PvImageBox, PvIconBox;
        Image PvIcon;
        TextBlock PvTitle, PvSub, PvMeta, PvBody;
        StackPanel PvButtons;
        HotKey hotkey;
        System.Windows.Forms.NotifyIcon tray;
        Source[] sources;
        string cacheFile, histFile, sourcesFile;
        string cat = "All";
        DispatcherTimer clockTimer, indexTimer, hintTimer;
        int hintIdx;
        string[] hints = {
            "Ask \u201Cdo I have any Agatha Christie books?\u201D \u2014 you get a yes / no answer",
            "Try \u201Cone piece 21\u201D, \u201Citaly itinerary\u201D or \u201Ctax 2025\u201D",
            "Ctrl+Enter asks Claude  \u00B7  Ctrl+G searches Google in Chrome",
            "Tab switches category  \u00B7  Ctrl+O shows the file in its folder",
            "Type an app name to launch it  \u00B7  Esc hides this window"
        };
        string[] catNames = { "All", "Apps", "Movies", "TV Shows", "Anime", "Animation", "Manga", "Comics", "Books", "Photos", "Trips", "Magazines", "Reference", "Travel", "Projects", "Docs", "Desktop", "Downloads", "Folders" };

        const string Xaml = @"
<Window xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
        xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        Title='Home' WindowStyle='None' ResizeMode='NoResize' Background='#0B0E14'
        FontFamily='Segoe UI Variable Text, Segoe UI' ShowInTaskbar='True'
        TextOptions.TextFormattingMode='Display' UseLayoutRounding='True'>
  <Window.Resources>
    <Style x:Key='Row' TargetType='ListBoxItem'>
      <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='ListBoxItem'>
            <Border x:Name='b' CornerRadius='12' Background='Transparent' Margin='0,1' Padding='8,0'>
              <ContentPresenter/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Background' Value='#14FFFFFF'/></Trigger>
              <Trigger Property='IsSelected' Value='True'><Setter TargetName='b' Property='Background' Value='#405B8CFF'/></Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key='Chip' TargetType='ListBoxItem'>
      <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
      <Setter Property='Cursor' Value='Hand'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='ListBoxItem'>
            <Border x:Name='b' CornerRadius='15' Background='#1AFFFFFF' BorderBrush='#22FFFFFF' BorderThickness='1' Padding='14,5' Margin='0,0,8,8'>
              <TextBlock x:Name='t' Text='{Binding}' FontSize='13' Foreground='#C9D1E0'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Background' Value='#2EFFFFFF'/></Trigger>
              <Trigger Property='IsSelected' Value='True'>
                <Setter TargetName='b' Property='Background' Value='#F2F4F8'/>
                <Setter TargetName='t' Property='Foreground' Value='#0B0E14'/>
                <Setter TargetName='t' Property='FontWeight' Value='SemiBold'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key='Tile' TargetType='ListBoxItem'>
      <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
      <Setter Property='Cursor' Value='Hand'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='ListBoxItem'>
            <Border x:Name='b' CornerRadius='14' Background='Transparent' Padding='6' Margin='0,0,10,0'>
              <ContentPresenter/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Background' Value='#1FFFFFFF'/></Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key='Btn' TargetType='Button'>
      <Setter Property='Cursor' Value='Hand'/>
      <Setter Property='Foreground' Value='#E8ECF3'/>
      <Setter Property='FontSize' Value='13'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='Button'>
            <Border x:Name='b' CornerRadius='10' Background='#22FFFFFF' BorderBrush='#26FFFFFF' BorderThickness='1' Padding='14,7' Margin='0,0,8,8'>
              <ContentPresenter HorizontalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Background' Value='#3AFFFFFF'/></Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <DataTemplate x:Key='RowTpl'>
      <Grid Height='58'>
        <Grid.ColumnDefinitions><ColumnDefinition Width='52'/><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>
        <Border Width='40' Height='40' CornerRadius='8' ClipToBounds='True' HorizontalAlignment='Left'>
          <Grid>
            <Image Source='{Binding Visual}' Stretch='UniformToFill' RenderOptions.BitmapScalingMode='HighQuality'/>
            <TextBlock Text='{Binding Glyph}' FontFamily='Segoe Fluent Icons, Segoe MDL2 Assets' FontSize='22' Foreground='{Binding Color}' HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Grid>
        </Border>
        <StackPanel Grid.Column='1' VerticalAlignment='Center'>
          <TextBlock Text='{Binding Name}' Foreground='#F2F4F8' FontSize='15.5' TextTrimming='CharacterEllipsis'/>
          <TextBlock Text='{Binding Sub}' Foreground='#8A93A6' FontSize='11.5' Margin='0,2,0,0' TextTrimming='CharacterEllipsis'/>
        </StackPanel>
        <Border Grid.Column='2' CornerRadius='10' Padding='10,3' Margin='10,0,2,0' VerticalAlignment='Center' Background='{Binding Tint}'>
          <TextBlock Text='{Binding Cat}' FontSize='11.5' FontWeight='SemiBold' Foreground='{Binding Color}'/>
        </Border>
      </Grid>
    </DataTemplate>
    <DataTemplate x:Key='CoverTpl'>
      <StackPanel Width='132'>
        <Border Width='132' Height='196' CornerRadius='10' Background='#22FFFFFF' ClipToBounds='True'>
          <Border.Effect><DropShadowEffect BlurRadius='16' ShadowDepth='4' Opacity='0.45'/></Border.Effect>
          <Grid>
            <TextBlock Text='{Binding Name}' TextWrapping='Wrap' Margin='10' Foreground='#C9D1E0' FontSize='13' VerticalAlignment='Center' TextAlignment='Center'/>
            <Image Source='{Binding Thumb}' Stretch='UniformToFill' RenderOptions.BitmapScalingMode='HighQuality'/>
          </Grid>
        </Border>
        <TextBlock Text='{Binding Name}' Foreground='#E8ECF3' FontSize='12.5' Margin='2,8,2,0' TextTrimming='CharacterEllipsis'/>
        <TextBlock Text='{Binding Sub}' Foreground='#7F889B' FontSize='11' Margin='2,1,2,0' TextTrimming='CharacterEllipsis'/>
      </StackPanel>
    </DataTemplate>
    <DataTemplate x:Key='WideTpl'>
      <StackPanel Width='250'>
        <Border Width='250' Height='156' CornerRadius='12' Background='#22FFFFFF' ClipToBounds='True'>
          <Border.Effect><DropShadowEffect BlurRadius='16' ShadowDepth='4' Opacity='0.45'/></Border.Effect>
          <Grid>
            <Image Source='{Binding Visual}' Stretch='UniformToFill' RenderOptions.BitmapScalingMode='HighQuality'/>
            <TextBlock Text='{Binding Glyph}' FontFamily='Segoe Fluent Icons, Segoe MDL2 Assets' FontSize='40' Foreground='{Binding Color}' HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Grid>
        </Border>
        <TextBlock Text='{Binding Name}' Foreground='#E8ECF3' FontSize='13' Margin='2,8,2,0' TextTrimming='CharacterEllipsis'/>
        <TextBlock Text='{Binding Sub}' Foreground='#7F889B' FontSize='11' Margin='2,1,2,0' TextTrimming='CharacterEllipsis'/>
      </StackPanel>
    </DataTemplate>
    <DataTemplate x:Key='AppTpl'>
      <StackPanel Width='92'>
        <Border Width='72' Height='72' CornerRadius='18' Background='#1FFFFFFF' HorizontalAlignment='Center'>
          <Image Source='{Binding Visual}' Width='44' Height='44' RenderOptions.BitmapScalingMode='HighQuality'/>
        </Border>
        <TextBlock Text='{Binding Name}' Foreground='#E8ECF3' FontSize='12' Margin='0,8,0,0' TextAlignment='Center' TextTrimming='CharacterEllipsis'/>
      </StackPanel>
    </DataTemplate>
  </Window.Resources>

  <Grid>
    <Image x:Name='Bg' Stretch='UniformToFill' Opacity='0'>
      <Image.Effect><BlurEffect Radius='18' RenderingBias='Performance'/></Image.Effect>
    </Image>
    <Rectangle>
      <Rectangle.Fill>
        <LinearGradientBrush StartPoint='0,0' EndPoint='0,1'>
          <GradientStop Color='#B30B0E14' Offset='0'/>
          <GradientStop Color='#D90B0E14' Offset='0.45'/>
          <GradientStop Color='#F20B0E14' Offset='1'/>
        </LinearGradientBrush>
      </Rectangle.Fill>
    </Rectangle>

    <Grid Margin='56,40,56,24'>
      <Grid.RowDefinitions>
        <RowDefinition Height='Auto'/><RowDefinition Height='Auto'/><RowDefinition Height='*'/><RowDefinition Height='Auto'/>
      </Grid.RowDefinitions>

      <!-- top bar -->
      <DockPanel>
        <StackPanel DockPanel.Dock='Left'>
          <TextBlock x:Name='Clock' FontSize='64' FontWeight='Light' Foreground='#F5F7FA'/>
          <TextBlock x:Name='DateText' FontSize='16' Foreground='#B5BDCC' Margin='4,0,0,0'/>
        </StackPanel>
        <StackPanel DockPanel.Dock='Right' Orientation='Horizontal' HorizontalAlignment='Right' VerticalAlignment='Top'>
          <StackPanel x:Name='UserBtns' Orientation='Horizontal'/>
          <Button x:Name='BCat' Style='{StaticResource Btn}' Content='Drive catalogue'/>
          <Button x:Name='BSort' Style='{StaticResource Btn}' Content='Sort new files'/>
          <Button x:Name='BRefresh' Style='{StaticResource Btn}' Content='Refresh locations' ToolTip='Re-scan all connected drives so moved or renamed files are found again'/>
          <Button x:Name='BDown' Style='{StaticResource Btn}' Content='Downloads'/>
          <Button x:Name='BClaude' Style='{StaticResource Btn}' Content='Claude'/>
          <Button x:Name='BHide' Style='{StaticResource Btn}' Content='Hide  (Esc)'/>
        </StackPanel>
        <Grid/>
      </DockPanel>

      <!-- search -->
      <StackPanel Grid.Row='1' HorizontalAlignment='Center' Width='920' Margin='0,10,0,18'>
        <TextBlock x:Name='Greeting' FontSize='18' Foreground='#B5BDCC' HorizontalAlignment='Center'/>
        <TextBlock x:Name='Prompt' Text='What do you want to find?' FontSize='34' FontWeight='SemiBold' Foreground='#F5F7FA' HorizontalAlignment='Center' Margin='0,4,0,18'/>
        <Border CornerRadius='20' Background='#26FFFFFF' BorderBrush='#33FFFFFF' BorderThickness='1' Padding='22,12'>
          <Border.Effect><DropShadowEffect BlurRadius='30' ShadowDepth='6' Opacity='0.5'/></Border.Effect>
          <Grid>
            <TextBlock Text='&#xE721;' FontFamily='Segoe Fluent Icons, Segoe MDL2 Assets' FontSize='22' Foreground='#9DB8FF' VerticalAlignment='Center'/>
            <TextBox x:Name='Q' Margin='38,0,230,0' FontSize='24' Foreground='#F5F7FA' Background='Transparent' BorderThickness='0'
                     CaretBrush='#9DB8FF' SelectionBrush='#5B8CFF' VerticalContentAlignment='Center' Padding='0,2'/>
            <TextBlock x:Name='Ph' Margin='40,0,230,0' FontSize='24' Foreground='#66FFFFFF' IsHitTestVisible='False' VerticalAlignment='Center'
                       Text='Search, or ask &#x201C;do I have &#x2026;?&#x201D;'/>
            <Button x:Name='BAsk' Style='{StaticResource Btn}' HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,8,-8,0' Content='Ask Claude  &#x00B7;  Ctrl+Enter'/>
          </Grid>
        </Border>
        <TextBlock x:Name='Hint' FontSize='12.5' Foreground='#8A93A6' HorizontalAlignment='Center' Margin='0,12,0,8'/>
        <ListBox x:Name='Cats' Background='Transparent' BorderThickness='0' ItemContainerStyle='{StaticResource Chip}'
                 ScrollViewer.HorizontalScrollBarVisibility='Disabled' HorizontalAlignment='Center' Focusable='False'>
          <ListBox.ItemsPanel><ItemsPanelTemplate><WrapPanel HorizontalAlignment='Center'/></ItemsPanelTemplate></ListBox.ItemsPanel>
        </ListBox>
      </StackPanel>

      <!-- home shelves -->
      <Grid x:Name='HomeView' Grid.Row='2'>
        <ScrollViewer VerticalScrollBarVisibility='Hidden' Focusable='False'>
          <StackPanel x:Name='Shelves' Margin='0,4,0,0'/>
        </ScrollViewer>
      </Grid>

      <!-- results + preview -->
      <Grid x:Name='ResultsView' Grid.Row='2' Visibility='Collapsed'>
        <Grid.ColumnDefinitions><ColumnDefinition Width='5*'/><ColumnDefinition Width='28'/><ColumnDefinition Width='4*'/></Grid.ColumnDefinitions>
        <Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition Height='*'/></Grid.RowDefinitions>
        <Border x:Name='Ans' Grid.ColumnSpan='3' CornerRadius='18' Background='#400B0E14' BorderBrush='#33FFFFFF' BorderThickness='1' Padding='22,16' Margin='0,0,0,16' Visibility='Collapsed'>
          <DockPanel>
            <Border x:Name='AnsPill' DockPanel.Dock='Left' CornerRadius='14' Padding='16,6' VerticalAlignment='Top' Margin='0,2,18,0'>
              <TextBlock x:Name='AnsVerdict' FontSize='18' FontWeight='SemiBold' Foreground='#0B0E14'/>
            </Border>
            <StackPanel>
              <TextBlock x:Name='AnsText' FontSize='18' Foreground='#F5F7FA' TextWrapping='Wrap'/>
              <TextBlock x:Name='AnsDetail' FontSize='13.5' Foreground='#B5BDCC' TextWrapping='Wrap' Margin='0,6,0,0' MaxHeight='60' TextTrimming='CharacterEllipsis'/>
              <WrapPanel x:Name='AnsLinks' Margin='0,10,0,0'/>
            </StackPanel>
          </DockPanel>
        </Border>
        <Border Grid.Row='1' CornerRadius='18' Background='#1F0B0E14' BorderBrush='#1AFFFFFF' BorderThickness='1' Padding='10'>
          <ListBox x:Name='L' Background='Transparent' BorderThickness='0' ItemContainerStyle='{StaticResource Row}' ItemTemplate='{StaticResource RowTpl}'
                   ScrollViewer.HorizontalScrollBarVisibility='Disabled' ScrollViewer.VerticalScrollBarVisibility='Hidden'
                   VirtualizingPanel.IsVirtualizing='True' VirtualizingPanel.ScrollUnit='Pixel' Focusable='False'/>
        </Border>
        <Border Grid.Row='1' Grid.Column='2' CornerRadius='18' Background='#330B0E14' BorderBrush='#1AFFFFFF' BorderThickness='1' Padding='26'>
          <ScrollViewer VerticalScrollBarVisibility='Hidden' Focusable='False'>
            <StackPanel>
              <Border x:Name='PvImageBox' CornerRadius='14' ClipToBounds='True' MaxHeight='420' HorizontalAlignment='Left' Margin='0,0,0,18'>
                <Border.Effect><DropShadowEffect BlurRadius='24' ShadowDepth='6' Opacity='0.5'/></Border.Effect>
                <Image x:Name='PvImage' Stretch='Uniform' MaxHeight='420' RenderOptions.BitmapScalingMode='HighQuality'/>
              </Border>
              <Border x:Name='PvIconBox' Width='96' Height='96' CornerRadius='24' Background='#1FFFFFFF' HorizontalAlignment='Left' Margin='0,0,0,18'>
                <Image x:Name='PvIcon' Width='64' Height='64'/>
              </Border>
              <TextBlock x:Name='PvTitle' FontSize='24' FontWeight='SemiBold' Foreground='#F5F7FA' TextWrapping='Wrap'/>
              <TextBlock x:Name='PvSub' FontSize='13.5' Foreground='#9AA3B5' TextWrapping='Wrap' Margin='0,4,0,10'/>
              <TextBlock x:Name='PvMeta' FontSize='12.5' Foreground='#7F889B' TextWrapping='Wrap' Margin='0,0,0,14'/>
              <WrapPanel x:Name='PvButtonsWrap'><StackPanel x:Name='PvButtons' Orientation='Horizontal'/></WrapPanel>
              <TextBlock x:Name='PvBody' FontSize='13' Foreground='#C9D1E0' TextWrapping='Wrap' Margin='0,14,0,0' LineHeight='20'/>
            </StackPanel>
          </ScrollViewer>
        </Border>
      </Grid>

      <!-- footer -->
      <DockPanel Grid.Row='3' Margin='2,14,2,0'>
        <TextBlock x:Name='Caption' DockPanel.Dock='Left' FontSize='12' Foreground='#8A93A6'/>
        <TextBlock x:Name='HotkeyText' DockPanel.Dock='Right' FontSize='12' Foreground='#8A93A6' Margin='16,0,0,0'/>
        <TextBlock x:Name='Status' FontSize='12' Foreground='#6B7385' HorizontalAlignment='Right'/>
      </DockPanel>
    </Grid>
  </Grid>
</Window>";

        T F<T>(string name) where T : class { return win.FindName(name) as T; }

        public Home()
        {
            win = (Window)XamlReader.Parse(Xaml);
            win.Title = Settings.AppName;
            Q = F<TextBox>("Q"); Ph = F<TextBlock>("Ph"); Prompt = F<TextBlock>("Prompt"); Hint = F<TextBlock>("Hint");
            Clock = F<TextBlock>("Clock"); DateText = F<TextBlock>("DateText"); Greeting = F<TextBlock>("Greeting");
            Status = F<TextBlock>("Status"); Caption = F<TextBlock>("Caption"); HotkeyText = F<TextBlock>("HotkeyText");
            Cats = F<ListBox>("Cats"); L = F<ListBox>("L");
            HomeView = F<Grid>("HomeView"); ResultsView = F<Grid>("ResultsView"); Shelves = F<StackPanel>("Shelves");
            Bg = F<Image>("Bg"); PvImage = F<Image>("PvImage"); PvImageBox = F<Border>("PvImageBox");
            PvIconBox = F<Border>("PvIconBox"); PvIcon = F<Image>("PvIcon");
            PvTitle = F<TextBlock>("PvTitle"); PvSub = F<TextBlock>("PvSub"); PvMeta = F<TextBlock>("PvMeta"); PvBody = F<TextBlock>("PvBody");
            PvButtons = F<StackPanel>("PvButtons");
            Ans = F<Border>("Ans"); AnsPill = F<Border>("AnsPill"); AnsVerdict = F<TextBlock>("AnsVerdict"); AnsText = F<TextBlock>("AnsText");
            AnsDetail = F<TextBlock>("AnsDetail"); AnsLinks = F<WrapPanel>("AnsLinks");

            Thumbs.Init(win.Dispatcher);
            PlaceOnWorkArea();

            cacheFile = System.IO.Path.Combine(Program.DataDir, "index.tsv");
            histFile = System.IO.Path.Combine(Program.DataDir, "history.tsv");
            sourcesFile = System.IO.Path.Combine(Program.AppDir, "sources.json");
            sources = LoadSources();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Engine.LoadHistory(histFile);
            Engine.LoadCache(cacheFile);          // last session's index: the window is usable straight away
            // the drive catalogue (tens of thousands of files) and the film/show grouping load in the background
            var boot = new Thread(delegate()
            {
                try { Catalog.Init(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Drive Catalog")); }
                catch (Exception ex) { Log.Write("catalog init: " + ex.Message); }
                try
                {
                    MediaLib.Init(Catalog.Dir, delegate { win.Dispatcher.BeginInvoke(new Action(delegate { Engine.RebuildAsync(sources, cacheFile); })); });
                    MediaLib.Rebuild();
                }
                catch (Exception ex) { Log.Write("media init: " + ex.Message); }
                Catalog.Start(delegate { win.Dispatcher.BeginInvoke(new Action(delegate { Engine.RebuildAsync(sources, cacheFile); })); });
                Log.Write("startup: background load " + sw.ElapsedMilliseconds + " ms");
                win.Dispatcher.BeginInvoke(new Action(delegate { Engine.RebuildAsync(sources, cacheFile); }));
            });
            boot.IsBackground = true; boot.Priority = ThreadPriority.BelowNormal; boot.Start();

            Cats.ItemsSource = catNames;
            Cats.SelectedIndex = 0;
            Cats.SelectionChanged += delegate { if (Cats.SelectedItem != null) cat = (string)Cats.SelectedItem; Refresh(); Q.Focus(); };
            var typing = new DispatcherTimer(); typing.Interval = TimeSpan.FromMilliseconds(140);
            typing.Tick += delegate { typing.Stop(); Refresh(); };
            Q.TextChanged += delegate
            {
                Ph.Visibility = Q.Text.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
                typing.Stop(); typing.Start();          // search once typing pauses, so keystrokes are never dropped
            };
            L.SelectionChanged += delegate { ShowPreview(L.SelectedItem as Item); };
            L.MouseDoubleClick += delegate { Open(L.SelectedItem as Item, false); };
            win.PreviewKeyDown += OnKey;
            win.Closing += delegate(object s, CancelEventArgs e) { e.Cancel = true; Hide(); };

            var userBtns = (StackPanel)win.FindName("UserBtns");
            foreach (var sbtn in Settings.Buttons)
            {
                var ub = new Button(); ub.Style = (Style)win.Resources["Btn"]; ub.Content = sbtn.Label;
                var cfg = sbtn;
                ub.Click += delegate { Launch(Directory.Exists(cfg.Path) || File.Exists(cfg.Path) || cfg.Fallback.Length == 0 ? cfg.Path : cfg.Fallback); };
                userBtns.Children.Add(ub);
            }
            ((Button)win.FindName("BCat")).Click += delegate { OpenCatalog(); };
            ((Button)win.FindName("BSort")).Click += delegate { Sorter.Show(); };
            ((Button)win.FindName("BRefresh")).Click += delegate { RefreshLocations(); };
            ((Button)win.FindName("BDown")).Click += delegate { Launch(System.IO.Path.Combine(Environment.GetEnvironmentVariable("USERPROFILE"), "Downloads")); };
            ((Button)win.FindName("BClaude")).Click += delegate { Browse("https://claude.ai/new"); Hide(); };
            ((Button)win.FindName("BHide")).Click += delegate { Hide(); };
            ((Button)win.FindName("BAsk")).Click += delegate { AskClaude(); };

            clockTimer = new DispatcherTimer(); clockTimer.Interval = TimeSpan.FromSeconds(15);
            clockTimer.Tick += delegate { UpdateClock(); }; clockTimer.Start(); UpdateClock();
            hintTimer = new DispatcherTimer(); hintTimer.Interval = TimeSpan.FromSeconds(6);
            hintTimer.Tick += delegate { hintIdx = (hintIdx + 1) % hints.Length; FadeHint(); }; hintTimer.Start();
            Hint.Text = hints[0];
            indexTimer = new DispatcherTimer(); indexTimer.Interval = TimeSpan.FromMilliseconds(500);
            indexTimer.Tick += delegate
            {
                if (Engine.Fresh) { Engine.Fresh = false; if (shelvesAt != DateTime.MinValue && ShelvesStale()) BuildShelves(); Refresh(); }
                UpdateStatus();
            };
            indexTimer.Start();
            var inboxTimer = new DispatcherTimer(); inboxTimer.Interval = TimeSpan.FromMinutes(5);
            int lastInbox = -1;
            EventHandler checkInbox = delegate
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    int n = 0; bool auto = false;
                    try { n = Inbox.QuickCount(); auto = Inbox.SortWithoutAsking(); } catch { }
                    if (auto && n > 0) { try { n = Math.Max(0, n - Sorter.AutoSort()); } catch (Exception ex) { Log.Write("autosort: " + ex.Message); } }
                    win.Dispatcher.BeginInvoke(new Action(delegate
                    {
                        var b = (Button)win.FindName("BSort");
                        b.Content = n > 0 ? "Sort new files (" + n + ")" : "Sort new files";
                        if (n > 0 && n > lastInbox && lastInbox >= 0 && tray != null)
                            tray.ShowBalloonTip(6000, "New files to sort", n + " new file" + (n == 1 ? "" : "s") + " in Downloads. Click to name and file them.", System.Windows.Forms.ToolTipIcon.Info);
                        lastInbox = n;
                    }));
                });
            };
            inboxTimer.Tick += checkInbox; inboxTimer.Start();
            checkInbox(null, EventArgs.Empty);
            var reindex = new DispatcherTimer(); reindex.Interval = TimeSpan.FromMinutes(30);
            reindex.Tick += delegate { Engine.RebuildAsync(sources, cacheFile); }; reindex.Start();

            hotkey = new HotKey(win, Toggle);
            HotkeyText.Text = hotkey.Label.Length > 0 ? hotkey.Label + " opens " + Settings.AppName + " from anywhere" : "";
            SetupTray();
            EnsureStartup();
            LoadBackground();
            Log.Write("startup: window ready " + sw.ElapsedMilliseconds + " ms");
        }

        void FindDuplicates()
        {
            Status.Text = "Looking for duplicate films & episodes\u2026";
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<Duplicates.Pick> picks = null;
                try { picks = Duplicates.Find(); } catch (Exception ex) { Log.Write("duplicates: " + ex.Message); }
                win.Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (picks == null || picks.Count == 0) { MessageBox.Show("No duplicate films or episodes on the connected drives.", Settings.AppName); return; }
                    double gb = picks.Sum(p => p.DropSize) / 1e9;
                    string list = string.Join("\n", picks.Take(18).Select(p => "\u2022 " + p.Title + " \u2014 " + p.Why));
                    var r = MessageBox.Show(picks.Count + " lower-quality cop" + (picks.Count == 1 ? "y" : "ies") + " found (" + gb.ToString("0.0") + " GB):\n\n" + list +
                        (picks.Count > 18 ? "\n\u2026" : "") + "\n\nMove them to \"_Duplicates (review)\" on the same drive? Nothing is deleted \u2014 empty that folder yourself when you're happy.",
                        Settings.AppName, MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (r != MessageBoxResult.Yes) return;
                    ThreadPool.QueueUserWorkItem(delegate
                    {
                        int n = Duplicates.SetAside(picks);
                        win.Dispatcher.BeginInvoke(new Action(delegate { MessageBox.Show(n + " cop" + (n == 1 ? "y" : "ies") + " moved to \"_Duplicates (review)\".", Settings.AppName); }));
                    });
                }));
            });
        }

        // Re-scan every connected drive and rebuild the index, so files that were moved or renamed are found again.
        void RefreshLocations()
        {
            sources = LoadSources();
            Engine.RebuildAsync(sources, cacheFile);
            Catalog.RescanNow();
            Status.Text = "Refreshing file locations \u2014 re-scanning connected drives\u2026";
        }

        // ---------------------------------------------------------- window state
        void PlaceOnWorkArea()
        {
            var wa = SystemParameters.WorkArea;
            win.Left = wa.Left; win.Top = wa.Top; win.Width = wa.Width; win.Height = wa.Height;
        }

        public void Prepare() { }

        public void Summon()
        {
            PlaceOnWorkArea();
            if (!win.IsVisible) { win.Opacity = 0; win.Show(); }
            win.WindowState = WindowState.Normal;
            win.Activate();
            win.Topmost = true; win.Topmost = false;
            win.BeginAnimation(Window.OpacityProperty, new DoubleAnimation(win.Opacity, 1, TimeSpan.FromMilliseconds(180)));
            Q.Focus(); Keyboard.Focus(Q); Q.SelectAll();
            UpdateClock();
            // the window draws first; the shelves (a hundred-odd cards) fill in right after
            if (Q.Text.Length == 0)
                win.Dispatcher.BeginInvoke(new Action(delegate { if (ShelvesStale()) BuildShelves(); }), DispatcherPriority.Background);
        }

        public void Hide()
        {
            win.Hide();
        }

        void Toggle()
        {
            if (win.IsVisible && win.IsActive) Hide(); else Summon();
        }

        // ---------------------------------------------------------- sources
        Source[] LoadSources()
        {
            var list = new List<Source>();
            try
            {
                var ser = new System.Web.Script.Serialization.JavaScriptSerializer();
                var root = ser.DeserializeObject(File.ReadAllText(sourcesFile, Encoding.UTF8)) as Dictionary<string, object>;
                foreach (var o in (object[])root["sources"])
                {
                    var d = o as Dictionary<string, object>;
                    var s = new Source();
                    s.Root = Environment.ExpandEnvironmentVariables((string)d["root"]);
                    s.Cat = (string)d["cat"];
                    if (d.ContainsKey("depth")) s.Depth = Convert.ToInt32(d["depth"]);
                    if (d.ContainsKey("dirLevels")) s.DirLevels = Convert.ToInt32(d["dirLevels"]);
                    if (d.ContainsKey("files")) s.Files = Convert.ToBoolean(d["files"]);
                    if (d.ContainsKey("mode")) s.Mode = (string)d["mode"];
                    if (d.ContainsKey("exts")) s.Exts = ((object[])d["exts"]).Select(x => ((string)x).ToLowerInvariant()).ToArray();
                    if (d.ContainsKey("exclude")) s.Exclude = ((object[])d["exclude"]).Select(x => (string)x).ToArray();
                    list.Add(s);
                }
            }
            catch (Exception ex) { Log.Write("sources.json: " + ex.Message); }
            return list.ToArray();
        }

        // ---------------------------------------------------------- clock / hints / status
        void UpdateClock()
        {
            var now = DateTime.Now;
            Clock.Text = now.ToString("HH:mm");
            DateText.Text = now.ToString("dddd, d MMMM");
            int h = now.Hour;
            string part = h < 5 ? "Good night" : h < 12 ? "Good morning" : h < 17 ? "Good afternoon" : "Good evening";
            Greeting.Text = part + (Settings.UserName.Length > 0 ? ", " + Settings.UserName : "");
        }

        void FadeHint()
        {
            var a = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(250));
            a.Completed += delegate
            {
                Hint.Text = hints[hintIdx];
                Hint.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(350)));
            };
            Hint.BeginAnimation(UIElement.OpacityProperty, a);
        }

        void UpdateStatus()
        {
            int n = Engine.Items.Count;
            if (Engine.Busy && n == 0) { Status.Text = "Indexing your drives\u2026"; return; }
            Status.Text = n.ToString("N0") + " items" + (Engine.Busy ? "  \u00B7  refreshing\u2026" : "  \u00B7  updated " + Engine.BuiltAt.ToString("HH:mm"));
            string cs = Catalog.StatusText;
            if (string.IsNullOrEmpty(cs)) cs = MediaLib.StatusText;
            if (string.IsNullOrEmpty(cs)) cs = Posters.StatusText;
            if (string.IsNullOrEmpty(cs)) cs = Catalog.Summary();
            if (!string.IsNullOrEmpty(cs)) Status.Text = cs + "     " + Status.Text;
        }

        // ---------------------------------------------------------- background
        void LoadBackground()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string pick = null, caption = "";
                    var rnd = new Random();
                    var albums = new List<string>();
                    foreach (var y in Settings.BackgroundFolders)
                    {
                        try { if (Directory.Exists(y)) albums.AddRange(Directory.GetDirectories(y).Where(a => !Regex.IsMatch(System.IO.Path.GetFileName(a), "WhatsApp|Phone|Camera Roll|Profile|Misc|Screenshots", RegexOptions.IgnoreCase))); } catch { }
                    }
                    for (int tries = 0; tries < 6 && pick == null && albums.Count > 0; tries++)
                    {
                        string album = albums[rnd.Next(albums.Count)];
                        var days = new List<string>(); days.Add(album);
                        try { days.AddRange(Directory.GetDirectories(album)); } catch { }
                        string day = days[rnd.Next(days.Count)];
                        string[] jpgs;
                        try { jpgs = Directory.GetFiles(day, "*.jpg"); } catch { continue; }
                        if (jpgs.Length == 0) continue;
                        pick = jpgs[rnd.Next(jpgs.Length)];
                        caption = "\uD83D\uDCCD " + System.IO.Path.GetFileName(album) + "  \u00B7  " + System.IO.Path.GetFileNameWithoutExtension(pick);
                    }
                    if (pick == null)
                    {
                        var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
                        string wp = key != null ? key.GetValue("WallPaper") as string : null;
                        if (!string.IsNullOrEmpty(wp) && File.Exists(wp)) pick = wp;
                    }
                    if (pick == null) return;
                    var img = Thumbs.LoadFile(pick, 1600);
                    string cap = caption;
                    win.Dispatcher.BeginInvoke(new Action(delegate
                    {
                        Bg.Source = img;
                        Bg.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(900)));
                        Caption.Text = cap;
                    }));
                }
                catch (Exception ex) { Log.Write("background: " + ex.Message); }
            });
        }

        // ---------------------------------------------------------- shelves (home)
        DateTime shelvesAt = DateTime.MinValue;
        long shelvesSig;

        // what the shelves show, in one number: a re-index that found the same things leaves them alone
        long ShelvesSignature()
        {
            long sig = Engine.Items.Count;
            foreach (var it in Engine.Items)
                sig = sig * 31 + ((it.Path ?? "").GetHashCode() ^ it.Modified.GetHashCode() ^ (it.Thumbnail == null ? 0 : 17));
            return sig;
        }

        // never built, a different index, or long enough ago that "Jump back in" may have moved
        bool ShelvesStale()
        {
            return shelvesAt == DateTime.MinValue || (DateTime.Now - shelvesAt).TotalMinutes >= 2 || ShelvesSignature() != shelvesSig;
        }

        void BuildShelves()
        {
            shelvesAt = DateTime.Now;
            shelvesSig = ShelvesSignature();
            Shelves.Children.Clear();
            var items = Engine.Items;
            var recent = Engine.Recent(12);
            if (recent.Count > 0) AddShelf("Jump back in", recent, "WideTpl", 260);
            var pinned = items.Where(i => i.Mode == "pinned").OrderBy(i => i.Path, StringComparer.OrdinalIgnoreCase).ToList();
            if (pinned.Count > 0) AddShelf("Apps", pinned, "AppTpl", 96);
            AddBrowseTiles(items);
            var books = items.Where(i => i.Mode == "calibre").GroupBy(i => System.IO.Path.GetDirectoryName(i.Path))
                             .Select(g => g.First()).OrderByDescending(i => i.Modified).Take(18).ToList();
            if (books.Count > 0) AddShelf("Recently added books", books, "CoverTpl", 200);
            var manga = items.Where(i => i.Cat == "Manga" && i.IsDir).OrderBy(i => i.Name).ToList();
            if (manga.Count > 0) AddShelf("Manga", manga, "CoverTpl", 200);
            var comics = items.Where(i => i.Cat == "Comics" && i.IsDir).OrderBy(i => i.Name).ToList();
            if (comics.Count > 0) AddShelf("Comics", comics, "CoverTpl", 200);
            var watch = items.Where(i => i.Mode == "title").OrderByDescending(i => i.Modified).Take(18).ToList();
            if (watch.Count > 0) AddShelf("Recently added films & shows", watch, "CoverTpl", 200);
            var mags = items.Where(i => i.Cat == "Magazines" && !i.IsDir).OrderByDescending(i => i.Name).Take(14).ToList();
            if (mags.Count > 0) AddShelf("Magazines", mags, "CoverTpl", 200);
            var guides = items.Where(i => i.Cat == "Travel" && !i.IsDir).OrderByDescending(i => i.Modified).Take(14).ToList();
            if (guides.Count > 0) AddShelf("Travel guides", guides, "CoverTpl", 200);
            if (Shelves.Children.Count == 0)
            {
                var t = new TextBlock();
                t.Text = "Building your library index for the first time \u2014 shelves appear in a few seconds.";
                t.Foreground = Brush("#8A93A6"); t.FontSize = 14; t.HorizontalAlignment = HorizontalAlignment.Center; t.Margin = new Thickness(0, 40, 0, 0);
                Shelves.Children.Add(t);
            }
        }

        void AddBrowseTiles(List<Item> items)
        {
            var defs = new[] {
                new { Cat = "Movies",    Label = "Movies",    Art = "movies",    Unit = " films",  Pick = (Func<Item, bool>)(i => i.Mode == "title" && i.Cat == "Movies") },
                new { Cat = "TV Shows",  Label = "TV Shows",  Art = "tv",        Unit = " shows",  Pick = (Func<Item, bool>)(i => i.Mode == "title" && i.Cat == "TV") },
                new { Cat = "Anime",     Label = "Anime",     Art = "anime",     Unit = " titles", Pick = (Func<Item, bool>)(i => i.Mode == "title" && i.Cat == "Anime") },
                new { Cat = "Animation", Label = "Animation", Art = "animation", Unit = " titles", Pick = (Func<Item, bool>)(i => i.Mode == "title" && i.Cat == "Animation") },
                new { Cat = "Manga",     Label = "Manga",     Art = "manga",     Unit = " series", Pick = (Func<Item, bool>)(i => i.Cat == "Manga" && i.IsDir) },
                new { Cat = "Comics",    Label = "Comics",    Art = "comics",    Unit = " series", Pick = (Func<Item, bool>)(i => i.Cat == "Comics" && i.IsDir) },
                new { Cat = "Photos",    Label = "Photos",    Art = "photos",    Unit = " albums", Pick = (Func<Item, bool>)(i => i.Cat == "Photos" && i.IsDir && !Regex.IsMatch(i.Name ?? "", @"^\d{4}-\d{2}")) },
                new { Cat = "Trips",     Label = "Trips",     Art = "trips",     Unit = " trips",  Pick = (Func<Item, bool>)(i => i.Cat == "Photos" && i.IsDir && Regex.IsMatch(i.Name ?? "", @"^\d{4}-\d{2}")) } };
            var head = new TextBlock();
            head.Text = "Browse"; head.FontSize = 18; head.FontWeight = FontWeights.SemiBold;
            head.Foreground = Brush("#F2F4F8"); head.Margin = new Thickness(6, 14, 0, 10);
            Shelves.Children.Add(head);
            var row = new WrapPanel(); row.Margin = new Thickness(0, 0, 0, 6);
            foreach (var d in defs)
            {
                int count = items.Count(d.Pick);
                var tile = new Border();
                tile.Width = 166; tile.Height = 112; tile.CornerRadius = new CornerRadius(14); tile.Margin = new Thickness(6, 0, 8, 10);
                tile.ClipToBounds = true; tile.Cursor = Cursors.Hand;
                tile.Background = Brush("#22FFFFFF");
                var g = new Grid();
                // fixed, illustrated artwork per category (art\<name>.png) -- never a preview of a random item
                string artFile = System.IO.Path.Combine(Program.AppDir, "art", d.Art + ".png");
                if (File.Exists(artFile))
                {
                    try { var img = new Image(); img.Stretch = Stretch.UniformToFill; img.Source = Thumbs.LoadFile(artFile, 400); g.Children.Add(img); } catch { }
                }
                var shade = new Border();
                var lg = new LinearGradientBrush(); lg.StartPoint = new Point(0, 0.35); lg.EndPoint = new Point(0, 1);
                lg.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#000B0E14"), 0));
                lg.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#CC0B0E14"), 1));
                shade.Background = lg; g.Children.Add(shade);
                var sp = new StackPanel(); sp.VerticalAlignment = VerticalAlignment.Bottom; sp.Margin = new Thickness(12, 0, 10, 9);
                var tt = new TextBlock(); tt.Text = d.Label; tt.FontSize = 17; tt.FontWeight = FontWeights.SemiBold; tt.Foreground = Brush("#F5F7FA");
                var ct = new TextBlock(); ct.Text = count == 0 ? "nothing yet" : count.ToString("N0") + d.Unit; ct.FontSize = 11.5; ct.Foreground = Brush("#C9D1E0");
                sp.Children.Add(tt); sp.Children.Add(ct);
                g.Children.Add(sp);
                tile.Child = g;
                string cat = d.Cat;
                tile.MouseLeftButtonUp += delegate { int ix = Array.IndexOf(catNames, cat); if (ix >= 0) Cats.SelectedIndex = ix; Q.Focus(); };
                row.Children.Add(tile);
            }
            Shelves.Children.Add(row);
        }

        void AddShelf(string title, List<Item> list, string tpl, int thumbSize)
        {
            var head = new TextBlock();
            head.Text = title; head.FontSize = 18; head.FontWeight = FontWeights.SemiBold;
            head.Foreground = Brush("#F2F4F8"); head.Margin = new Thickness(6, 14, 0, 10);
            Shelves.Children.Add(head);
            var lb = new ListBox();
            lb.Background = Brushes.Transparent; lb.BorderThickness = new Thickness(0); lb.Focusable = false;
            lb.ItemContainerStyle = (Style)win.Resources["Tile"];
            lb.ItemTemplate = (DataTemplate)win.Resources[tpl];
            lb.ItemsPanel = (ItemsPanelTemplate)XamlReader.Parse("<ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><StackPanel Orientation='Horizontal'/></ItemsPanelTemplate>");
            ScrollViewer.SetHorizontalScrollBarVisibility(lb, ScrollBarVisibility.Hidden);
            ScrollViewer.SetVerticalScrollBarVisibility(lb, ScrollBarVisibility.Disabled);
            lb.PreviewMouseWheel += delegate(object s, MouseWheelEventArgs e)
            {
                // vertical wheel scrolls the page, shift+wheel scrolls the shelf
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) return;
                e.Handled = true;
                var parent = Shelves.Parent as ScrollViewer;
                if (parent != null) parent.ScrollToVerticalOffset(parent.VerticalOffset - e.Delta);
            };
            lb.ItemsSource = list;
            lb.SelectionChanged += delegate
            {
                var it = lb.SelectedItem as Item;
                if (it == null) return;
                lb.SelectedItem = null;
                Open(it, false);
            };
            Shelves.Children.Add(lb);
            foreach (var it in list) LoadVisual(it, thumbSize);
        }

        void LoadVisual(Item it, int size)
        {
            if (it.Kind == "claude" || it.Kind == "web") return;
            if (it.Thumb != null) return;
            Thumbs.Request(it, size, delegate(ImageSource img)
            {
                if (img == null) return;
                if (it.Kind == "app") it.Icon = img; else it.Thumb = img;
            });
        }

        // ---------------------------------------------------------- results
        void Refresh()
        {
            string q = Q.Text.Trim();
            bool searching = q.Length > 0 || cat != "All";
            HomeView.Visibility = searching ? Visibility.Collapsed : Visibility.Visible;
            ResultsView.Visibility = searching ? Visibility.Visible : Visibility.Collapsed;
            Prompt.Visibility = searching ? Visibility.Collapsed : Visibility.Visible;
            Greeting.Visibility = Prompt.Visibility;
            if (!searching) { Ans.Visibility = Visibility.Collapsed; return; }
            var res = Engine.Search(q, cat, 120);
            Answer ans = null;
            try { ans = q.Length > 0 ? Engine.Ask(q, cat) : null; } catch (Exception ex) { Log.Write("ask: " + ex.Message); }
            ShowAnswer(ans);
            if (ans != null)
            {
                var merged = new List<Item>(ans.Hits.Take(80));
                var have = new HashSet<string>(merged.Select(x => x.Path), StringComparer.OrdinalIgnoreCase);
                if (ans.Verdict != "Yes") foreach (var x in res) if (have.Add(x.Path)) merged.Add(x);
                merged.Add(Engine.Special("claude", q)); merged.Add(Engine.Special("web", ans.Topic));
                res = merged;
            }
            else if (q.Length > 0)
            {
                var claude = Engine.Special("claude", q);
                var web = Engine.Special("web", q);
                bool question = Regex.IsMatch(q, @"^(what|how|why|who|when|where|which|can|should|is|are|do|does|explain|write|tell|help|compare)\b", RegexOptions.IgnoreCase) || q.EndsWith("?");
                if (question || res.Count == 0) { res.Insert(0, claude); res.Add(web); }
                else { res.Add(claude); res.Add(web); }
            }
            L.ItemsSource = res;
            if (res.Count > 0) { L.SelectedIndex = 0; L.ScrollIntoView(L.SelectedItem); }
            int n = 0;
            foreach (var it in res) { if (n++ > 40) break; LoadVisual(it, 96); }
        }

        void ShowAnswer(Answer a)
        {
            lastAns = a;
            AnsLinks.Children.Clear();
            if (a == null) { Ans.Visibility = Visibility.Collapsed; return; }
            Ans.Visibility = Visibility.Visible;
            AnsVerdict.Text = a.Verdict;
            string col = a.Verdict == "Yes" ? "#7EE2A8" : a.Verdict == "No" ? "#FF8A80" : "#FFD180";
            AnsPill.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(col));
            AnsText.Text = a.Text;
            AnsDetail.Text = a.Detail ?? "";
            if (a.Best != null)
            {
                var best = a.Best;
                AnsLink(best.Kind == "app" ? "Launch " + Short(best.Name) : "Open " + Short(best.Name), delegate { Open(best, false); });
                if (!best.IsDir && best.Kind != "app") AnsLink("Show in folder", delegate { Open(best, true); });
            }
            foreach (var l in a.Links)
            {
                string url = l[1];
                AnsLink(l[0] + "  \u2197", delegate { Browse(url); });
            }
        }

        static string Short(string s) { return s.Length > 34 ? s.Substring(0, 32) + "\u2026" : s; }

        void AnsLink(string text, Action act)
        {
            var b = new Button();
            b.Content = text; b.Style = (Style)win.Resources["Btn"]; b.Focusable = false;
            b.Click += delegate { act(); };
            AnsLinks.Children.Add(b);
        }

        void Move(int d)
        {
            int n = L.Items.Count; if (n == 0) return;
            int i = Math.Max(0, Math.Min(n - 1, L.SelectedIndex + d));
            L.SelectedIndex = i; L.ScrollIntoView(L.SelectedItem);
            var list = L.ItemsSource as List<Item>;
            if (list != null) for (int k = i; k < Math.Min(list.Count, i + 12); k++) LoadVisual(list[k], 96);
        }

        // ---------------------------------------------------------- preview
        void ShowPreview(Item it)
        {
            PvButtons.Children.Clear();
            PvImage.Source = null; PvIcon.Source = null;
            PvImageBox.Visibility = Visibility.Collapsed; PvIconBox.Visibility = Visibility.Collapsed;
            PvBody.Text = ""; PvMeta.Text = "";
            if (it == null) { PvTitle.Text = ""; PvSub.Text = ""; return; }
            PvTitle.Text = it.Name;
            PvSub.Text = it.Sub;

            if (it.Kind == "claude")
            {
                PvBody.Text = "Claude opens in Chrome with your question already typed in. Good for questions, explanations, drafting messages, or searching the web with Claude.";
                AddBtn("Ask Claude", delegate { Open(it, false); });
                AddBtn("Search Google instead", delegate { Open(Engine.Special("web", Q.Text), false); });
                return;
            }
            if (it.Kind == "web")
            {
                AddBtn("Search Google in Chrome", delegate { Open(it, false); });
                AddBtn("Ask Claude instead", delegate { AskClaude(); });
                return;
            }

            // meta + body: read the disk off the UI thread (a sleeping USB drive can take seconds to answer)
            PvMeta.Text = it.Path;
            var item = it;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string meta = item.Path, body = null;
                try
                {
                    var sb = new StringBuilder();
                    if (item.IsDir)
                    {
                        var di = new DirectoryInfo(item.Path);
                        var kids = di.GetFileSystemInfos();
                        sb.Append(kids.Length + " items  \u00B7  modified " + di.LastWriteTime.ToString("d MMM yyyy"));
                        var names = kids.OrderBy(k => k.Name).Take(14).Select(k => (k is DirectoryInfo ? "\uD83D\uDCC1 " : "    ") + k.Name);
                        body = string.Join("\n", names) + (kids.Length > 14 ? "\n    \u2026" : "");
                    }
                    else if (File.Exists(item.Path))
                    {
                        var fi = new FileInfo(item.Path);
                        sb.Append(Size(fi.Length) + "  \u00B7  modified " + fi.LastWriteTime.ToString("d MMM yyyy, HH:mm"));
                    }
                    else if (item.Offline) sb.Append("On " + item.Where + " \u2014 not connected right now. Known from the drive catalogue.");
                    else sb.Append("Not reachable right now (drive unplugged or file moved). Opening it will look for where it went.");
                    sb.Append("\n" + item.Path);
                    meta = sb.ToString();
                    if (item.Mode == "calibre") body = BookBlurb(item.Path);
                    else if (item.Kind == "text") body = Head(item.Path, 40);
                }
                catch { }
                win.Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (L.SelectedItem != item) return;
                    PvMeta.Text = meta;
                    if (body != null) PvBody.Text = body;
                }));
            });

            AddBtn(it.Kind == "app" ? "Launch" : it.IsDir ? "Open folder" : "Open", delegate { Open(it, false); });
            if (!it.IsDir && it.Kind != "app") AddBtn("Show in folder", delegate { Open(it, true); });
            AddBtn("Copy path", delegate { try { Clipboard.SetText(it.Path); } catch { } });
            AddBtn("Ask Claude about it", delegate { AskClaudeAbout(it); });

            // visual
            Thumbs.Request(it, 640, delegate(ImageSource img)
            {
                if (L.SelectedItem != it) return;
                if (img == null) return;
                if (it.Kind == "app" || Thumbs.IsIcon(img))
                {
                    PvIcon.Source = img; PvIconBox.Visibility = Visibility.Visible;
                }
                else
                {
                    PvImage.Source = img; PvImageBox.Visibility = Visibility.Visible;
                    PvImageBox.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
                }
            });
        }

        void AddBtn(string text, Action a)
        {
            var b = new Button();
            b.Content = text; b.Style = (Style)win.Resources["Btn"]; b.Focusable = false;
            b.Click += delegate { a(); };
            PvButtons.Children.Add(b);
        }

        static string Size(long b)
        {
            if (b > 1L << 30) return (b / (double)(1L << 30)).ToString("0.0") + " GB";
            if (b > 1L << 20) return (b / (double)(1L << 20)).ToString("0.0") + " MB";
            if (b > 1L << 10) return (b / 1024.0).ToString("0") + " KB";
            return b + " bytes";
        }

        static string Head(string path, int lines)
        {
            try
            {
                var sb = new StringBuilder(); int n = 0;
                using (var r = new StreamReader(path))
                {
                    string line;
                    while ((line = r.ReadLine()) != null && n < lines) { sb.AppendLine(line.Length > 160 ? line.Substring(0, 160) + "\u2026" : line); n++; }
                }
                return sb.ToString();
            }
            catch { return ""; }
        }

        static string BookBlurb(string path)
        {
            try
            {
                string opf = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path) ?? "", "metadata.opf");
                if (!File.Exists(opf)) return "";
                string x = File.ReadAllText(opf);
                var parts = new List<string>();
                var series = Regex.Match(x, "name=\"calibre:series\" content=\"([^\"]+)\"");
                var idx = Regex.Match(x, "name=\"calibre:series_index\" content=\"([^\"]+)\"");
                if (series.Success) parts.Add("Series: " + WebUtilityDecode(series.Groups[1].Value) + (idx.Success ? " #" + idx.Groups[1].Value : ""));
                var date = Regex.Match(x, "<dc:date>(\\d{4})");
                if (date.Success && date.Groups[1].Value != "0101") parts.Add("Published " + date.Groups[1].Value);
                var desc = Regex.Match(x, "<dc:description>(.*?)</dc:description>", RegexOptions.Singleline);
                string d = desc.Success ? Regex.Replace(WebUtilityDecode(desc.Groups[1].Value), "<[^>]+>", " ") : "";
                d = Regex.Replace(d, "\\s+", " ").Trim();
                if (d.Length > 900) d = d.Substring(0, 900) + "\u2026";
                return string.Join("   \u00B7   ", parts) + (d.Length > 0 ? "\n\n" + d : "");
            }
            catch { return ""; }
        }

        static string WebUtilityDecode(string s) { return System.Net.WebUtility.HtmlDecode(System.Net.WebUtility.HtmlDecode(s)); }

        // ---------------------------------------------------------- actions
        void OnKey(object sender, KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            switch (e.Key)
            {
                case Key.Escape:
                    if (Q.Text.Length > 0 || cat != "All") { Q.Text = ""; Cats.SelectedIndex = 0; } else Hide();
                    e.Handled = true; break;
                case Key.Down: Move(1); e.Handled = true; break;
                case Key.Up: Move(-1); e.Handled = true; break;
                case Key.PageDown: Move(8); e.Handled = true; break;
                case Key.PageUp: Move(-8); e.Handled = true; break;
                case Key.Return:
                    if (ctrl) AskClaude();
                    else Open(L.SelectedItem as Item, false);
                    e.Handled = true; break;
                case Key.Tab:
                    int i = Cats.SelectedIndex + (shift ? -1 : 1);
                    if (i < 0) i = catNames.Length - 1; if (i >= catNames.Length) i = 0;
                    Cats.SelectedIndex = i; e.Handled = true; break;
                case Key.F5:
                    Engine.RebuildAsync(sources = LoadSources(), cacheFile); UpdateStatus(); e.Handled = true; break;
                case Key.G:
                    if (ctrl && Q.Text.Trim().Length > 0) { Open(Engine.Special("web", Q.Text), false); e.Handled = true; }
                    break;
                case Key.O:
                    if (ctrl) { Open(L.SelectedItem as Item, true); e.Handled = true; }
                    break;
            }
        }

        void AskClaude()
        {
            string q = Q.Text.Trim();
            if (q.Length == 0) { Browse("https://claude.ai/new"); Hide(); return; }
            Open(Engine.Special("claude", q), false);
        }

        void AskClaudeAbout(Item it)
        {
            string q = Q.Text.Trim();
            string prompt = "I have a file called \u201C" + System.IO.Path.GetFileName(it.Path) + "\u201D (" + it.Cat + ", " + it.Sub.Replace("  \u00B7  ", ", ").Replace("  \u203A  ", "\\") + "). " +
                            (q.Length > 0 ? "I searched for \u201C" + q + "\u201D. " : "") + "Help me with it: ";
            Browse("https://claude.ai/new?q=" + Uri.EscapeDataString(prompt));
            Hide();
        }

        void Open(Item it, bool reveal)
        {
            if (it == null) return;
            try
            {
                if (it.Kind == "claude" || it.Kind == "web") { Browse(it.Path); Hide(); return; }
                if (reveal)
                {
                    Engine.Record(it.Path);
                    Process.Start("explorer.exe", "/select,\"" + it.Path + "\"");
                    Hide(); return;
                }
                var item = it;
                PvMeta.Text = "Opening\u2026";
                ThreadPool.QueueUserWorkItem(delegate
                {
                    string path = item.Path, msg = null; bool refresh = false;
                    try
                    {
                        if (!File.Exists(path) && !Directory.Exists(path))
                        {
                            string moved = Catalog.FindMoved(path, item.IsDir);
                            if (moved != null) { Log.Write("open: " + path + " moved to " + moved); path = moved; refresh = true; }
                            else if (item.Offline) { msg = "This is on " + item.Where + ", which isn't connected. Plug the drive in and it will open from here."; path = null; }
                            else { msg = "Can't find this where it used to be \u2014 refreshing file locations now; try again in a minute."; path = null; refresh = true; }
                        }
                    }
                    catch (Exception ex) { msg = "Couldn't open it: " + ex.Message; path = null; }
                    string p = path, m = msg; bool r = refresh;
                    win.Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (m != null) PvMeta.Text = m;
                        if (r) RefreshLocations();
                        if (p != null) { Engine.Record(p); Launch(p); }
                    }));
                });
            }
            catch (Exception ex) { PvMeta.Text = "Couldn't open it: " + ex.Message; }
        }

        void OpenCatalog()
        {
            try
            {
                string x = Catalog.XlsxPath;
                if (!File.Exists(Dashboard.HtmlPath)) { try { Dashboard.Write(); } catch (Exception ex) { Log.Write("dashboard: " + ex.Message); } }
                if (File.Exists(Dashboard.HtmlPath)) Browse(new Uri(Dashboard.HtmlPath).AbsoluteUri);
                else if (File.Exists(x)) Launch(x);
                else if (Catalog.Dir != null && Directory.Exists(Catalog.Dir)) Launch(Catalog.Dir);
                else Status.Text = "The drive catalogue is still being built\u2026";
            }
            catch (Exception ex) { Log.Write("catalog open: " + ex.Message); }
        }

        void Launch(string path)
        {
            Log.Write("open " + path);
            Hide();
            // ShellExecute can block for seconds (drive spin-up, slow app start) -- never on the UI thread
            var t = new Thread(delegate()
            {
                try { var psi = new ProcessStartInfo(path); psi.UseShellExecute = true; Process.Start(psi); }
                catch (Exception ex) { Log.Write("launch " + path + ": " + ex.Message); }
            });
            t.SetApartmentState(ApartmentState.STA); t.IsBackground = true; t.Start();
        }

        static string ChromePath()
        {
            var c = new[] {
                Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\Google\Chrome\Application\chrome.exe"),
                Environment.ExpandEnvironmentVariables(@"%ProgramFiles(x86)%\Google\Chrome\Application\chrome.exe"),
                Environment.ExpandEnvironmentVariables(@"%LocalAppData%\Google\Chrome\Application\chrome.exe") };
            return c.FirstOrDefault(File.Exists);
        }

        void Browse(string url)
        {
            try
            {
                string chrome = ChromePath();
                if (chrome != null) Process.Start(chrome, "\"" + url + "\"");
                else { var psi = new ProcessStartInfo(url); psi.UseShellExecute = true; Process.Start(psi); }
            }
            catch (Exception ex) { Log.Write("browse: " + ex.Message); }
        }

        static SolidColorBrush Brush(string hex) { return (SolidColorBrush)new BrushConverter().ConvertFromString(hex); }

        // ---------------------------------------------------------- tray + startup
        void SetupTray()
        {
            try
            {
                tray = new System.Windows.Forms.NotifyIcon();
                string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                tray.Icon = System.Drawing.Icon.ExtractAssociatedIcon(exe);
                tray.Text = Settings.AppName;
                var menu = new System.Windows.Forms.ContextMenuStrip();
                menu.Items.Add("Open " + Settings.AppName, null, delegate { Summon(); });
                menu.Items.Add("Re-index now", null, delegate { Engine.RebuildAsync(sources = LoadSources(), cacheFile); });
                menu.Items.Add("Edit indexed folders", null, delegate { Launch(sourcesFile); });
                menu.Items.Add("Sort new files\u2026", null, delegate { win.Dispatcher.BeginInvoke(new Action(Sorter.Show)); });
                menu.Items.Add("Open drive catalogue", null, delegate { win.Dispatcher.BeginInvoke(new Action(OpenCatalog)); });
                menu.Items.Add("Find duplicate films & episodes\u2026", null, delegate { win.Dispatcher.BeginInvoke(new Action(FindDuplicates)); });
                menu.Items.Add("Refresh file locations (re-scan drives)", null, delegate { win.Dispatcher.BeginInvoke(new Action(RefreshLocations)); });
                menu.Items.Add("Keep drive letters" + (Settings.DriveLetterHint.Length > 0 ? " (" + Settings.DriveLetterHint + ")" : "") + "\u2026", null, delegate { Launch(System.IO.Path.Combine(Program.AppDir, "tools", "Keep drive letters.bat")); });
                var start = new System.Windows.Forms.ToolStripMenuItem("Open at sign-in");
                start.Checked = File.Exists(StartupLink());
                start.Click += delegate
                {
                    if (File.Exists(StartupLink())) { try { File.Delete(StartupLink()); } catch { } }
                    else MakeShortcut(StartupLink(), "--startup", null);
                    start.Checked = File.Exists(StartupLink());
                };
                menu.Items.Add(start);
                menu.Items.Add("Quit", null, delegate { tray.Visible = false; if (hotkey != null) hotkey.Dispose(); Application.Current.Shutdown(); });
                tray.ContextMenuStrip = menu;
                tray.DoubleClick += delegate { Summon(); };
                tray.BalloonTipClicked += delegate { win.Dispatcher.BeginInvoke(new Action(Sorter.Show)); };
                tray.Visible = true;
            }
            catch (Exception ex) { Log.Write("tray: " + ex.Message); }
        }

        static string StartupLink()
        {
            return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), Settings.AppName + ".lnk");
        }

        void EnsureStartup()
        {
            try
            {
                string sortLnk = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Sort new files.lnk");
                if (!File.Exists(sortLnk)) MakeShortcut(sortLnk, "--sort", null);
            }
            catch { }
            string marker = System.IO.Path.Combine(Program.DataDir, "startup.ok");
            if (File.Exists(marker)) return;
            MakeShortcut(StartupLink(), "--startup", null);
            MakeShortcut(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Settings.AppName + ".lnk"), "", null);
            try { File.WriteAllText(marker, DateTime.Now.ToString("s")); } catch { }
        }

        static void MakeShortcut(string lnk, string args, string hotkeyText)
        {
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                object sh = Activator.CreateInstance(t);
                object sc = t.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, sh, new object[] { lnk });
                Type st = sc.GetType();
                string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                st.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { exe });
                st.InvokeMember("Arguments", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { args });
                st.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { Program.AppDir });
                st.InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { exe + ",0" });
                st.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { Settings.AppName + " - find anything, ask Claude" });
                if (hotkeyText != null) st.InvokeMember("Hotkey", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { hotkeyText });
                st.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, sc, null);
            }
            catch (Exception ex) { Log.Write("shortcut " + lnk + ": " + ex.Message); }
        }
    }
}
