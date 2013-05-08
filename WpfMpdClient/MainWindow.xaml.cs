﻿//    WpfMpdClient
//    Copyright (C) 2012, 2013 Paolo Iommarini
//    sakya_tg@yahoo.it
//
//    This program is free software; you can redistribute it and/or modify
//    it under the terms of the GNU General Public License as published by
//    the Free Software Foundation; either version 2 of the License, or
//    (at your option) any later version.
//
//    This program is distributed in the hope that it will be useful,
//    but WITHOUT ANY WARRANTY; without even the implied warranty of
//    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//    GNU General Public License for more details.
//
//    You should have received a copy of the GNU General Public License
//    along with this program; if not, write to the Free Software
//    Foundation, Inc., 51 Franklin St, Fifth Floor, Boston, MA  02110-1301  USA
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using GongSolutions.Wpf.DragDrop;
using Libmpc;
using System.Net;
using System.Timers;
using System.IO;
using System.ComponentModel;
using CsUpdater;
using System.Reflection;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

namespace WpfMpdClient
{
  public partial class MainWindow
  {
    public class MpdChannel : INotifyPropertyChanged
    {
      public event PropertyChangedEventHandler PropertyChanged;

      private string m_Name = string.Empty;
      private bool m_Subscribed = false;

      public string Name
      {
        get { return m_Name; }
        set
        {
          m_Name = value;
          OnPropertyChanged("Name");
        }
      }

      public bool Subscribed
      {
        get { return m_Subscribed; }
        set
        {
          m_Subscribed = value;
          OnPropertyChanged("Subscribed");
        }
      }

      protected void OnPropertyChanged(string name)
      {
        PropertyChangedEventHandler handler = PropertyChanged;
        if (handler != null) {
          handler(this, new PropertyChangedEventArgs(name));
        }
      }
    }

    static MainWindow This = null;

    #region Private members
    LastfmScrobbler m_LastfmScrobbler = null;
    Updater m_Updater = null;
    UpdaterApp m_App = null;
    Settings m_Settings = null;
    Mpc m_Mpc = null;
    Mpc m_MpcIdle = null;
    MpdStatus m_LastStatus = null;
    Timer m_StartTimer = null;
    Timer m_ReconnectTimer = null;
    List<MpdFile> m_Tracks = null;
    MpdFile m_CurrentTrack = null;
    DateTime m_CurrentTrackStart = DateTime.MinValue;
    About m_About = new About();
    System.Windows.Forms.NotifyIcon m_NotifyIcon = null;
    ContextMenu m_NotifyIconMenu = null;
    WindowState m_StoredWindowState = WindowState.Normal;
    bool m_Close = false;
    bool m_IgnoreDisconnect = false;
    MiniPlayerWindow m_MiniPlayer = null;
    List<string> m_Languages = new List<string>() { string.Empty, "fr", "de", "it", "jp", "pl", "pt", "ru", "es", "sv", "tr" };

    ArtDownloader m_ArtDownloader;
    ArtDownloader m_ArtistArtDownloader;
    ObservableCollection<ListboxEntry> m_ArtistsSource = new ObservableCollection<ListboxEntry>();
    ObservableCollection<ListboxEntry> m_GenresAlbumsSource = new ObservableCollection<ListboxEntry>();
    ObservableCollection<MpdMessage> m_Messages = new ObservableCollection<MpdMessage>();
    ObservableCollection<MpdChannel> m_Channels = new ObservableCollection<MpdChannel>();
    List<Expander> m_MessagesExpanders = new List<Expander>();
    #endregion

    public class Context : GongSolutions.Wpf.DragDrop.IDropTarget, GongSolutions.Wpf.DragDrop.IDragSource
    {
        public ObservableCollection<MpdFile> Playlist { get; set; }

        readonly Mpc m_Mpc = null;
        public Context(Mpc m_Mpc)
        {
            this.m_Mpc = m_Mpc;
            Primary = new Vis();
            Tracks = new Vis() { Empty = GridLength.Auto };
        }

        void GongSolutions.Wpf.DragDrop.IDropTarget.DragOver(GongSolutions.Wpf.DragDrop.IDropInfo dropInfo)
        {
            dropInfo.Effects = dropInfo.TargetItem is MpdFile || dropInfo.TargetItem is IEnumerable<MpdFile>
                ? DragDropEffects.Move
                : DragDropEffects.None;
            dropInfo.DropTargetAdorner = GongSolutions.Wpf.DragDrop.DropTargetAdorners.Insert;
        }

        void GongSolutions.Wpf.DragDrop.IDropTarget.Drop(GongSolutions.Wpf.DragDrop.IDropInfo dropInfo)
        {
            var target =
                dropInfo.TargetItem is MpdFile
                    ? dropInfo.TargetItem as MpdFile
                    : dropInfo.TargetItem is IEnumerable<MpdFile>
                        ? (dropInfo.TargetItem as IEnumerable<MpdFile>).FirstOrDefault()
                        : null;
            var items = dropInfo.DragInfo.SourceItems.OfType<MpdFile>().OrderBy(i => i.Pos).ToArray();
            if (target != null && items.FirstOrDefault() != null && m_Mpc.Connected)
            {
                var x = target.Pos - ((dropInfo.InsertPosition & RelativeInsertPosition.AfterTargetItem) == 0 ? 1 : 0);
                try
                {
                    foreach (var i in items)
                    {
                        var y = i.Pos;
                        if (y > x)
                            x++;
                        m_Mpc.MoveId(i.Id, x);
                    }
                }
                catch (Exception ex)
                {
                }
            }
        }

        void GongSolutions.Wpf.DragDrop.IDragSource.Dropped(GongSolutions.Wpf.DragDrop.IDropInfo dropInfo)
        {
            
        }

        void GongSolutions.Wpf.DragDrop.IDragSource.StartDrag(GongSolutions.Wpf.DragDrop.IDragInfo dragInfo)
        {
            if (dragInfo.SourceItems.OfType<object>().Count() > 1)
            {
                dragInfo.Effects = DragDropEffects.Move;
            }
        }

        public class Vis : INotifyPropertyChanged
        {
          public event PropertyChangedEventHandler PropertyChanged;
          private void NotifyPropertyChanged([CallerMemberName] String propertyName = "")
          {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
          }
          
          bool vis;
          public bool Visible { set { vis = value; NotifyPropertyChanged("Show"); NotifyPropertyChanged("Hide"); NotifyPropertyChanged("Size"); } }
          public Visibility Show { get { return vis ? Visibility.Visible : Visibility.Collapsed; } }
          public Visibility Hide { get { return !vis ? Visibility.Visible : Visibility.Collapsed; } }

          GridLength size = new GridLength(1, GridUnitType.Star);//GridLength.Auto;
          public GridLength Empty = new GridLength(0);
          public GridLength Size { get { return vis ? size : Empty; } set { size = value; } }
        }

        public Vis Tracks { get; set; }
        public Vis Primary { get; set; }
        public bool View
        {
            set
            {
                Tracks.Visible = value;
                Primary.Visible = !value;
            }
            get
            {
                return Primary.Show == Visibility.Collapsed;
            }
        }
    }

    protected readonly Context context;

    public MainWindow()
    {
      InitializeComponent();
      This = this;

      Title = string.Format("WpfMpdClient v.{0}", Assembly.GetExecutingAssembly().GetName().Version);
      stcAbout.DataContext = m_About;
      try {
        txtLicense.Text = File.ReadAllText("LICENSE.TXT");
      } catch (Exception){ 
        txtLicense.Text = "LICENSE not found!!!";
      }

      Settings.Instance = m_Settings = Settings.Deserialize(Settings.GetSettingsFileName());
      if (m_Settings != null) {
        txtServerAddress.Text = m_Settings.ServerAddress;
        txtServerPort.Text = m_Settings.ServerPort.ToString();
        txtPassword.Password = m_Settings.Password;
        chkAutoreconnect.IsChecked = m_Settings.AutoReconnect;
        chkShowStop.IsChecked = m_Settings.ShowStopButton;
        chkShowFilesystem.IsChecked = m_Settings.ShowFilesystemTab;
        chkMinimizeToTray.IsChecked = m_Settings.MinimizeToTray;
        chkCloseToTray.IsChecked = m_Settings.CloseToTray;
        chkShowMiniPlayer.IsChecked = m_Settings.ShowMiniPlayer;
        chkScrobbler.IsChecked = m_Settings.Scrobbler;
        cmbLastFmLang.SelectedIndex = m_Languages.IndexOf(m_Settings.InfoLanguage);
        if (cmbLastFmLang.SelectedIndex == -1)
          cmbLastFmLang.SelectedIndex = 0;
        cmbPlaylistStyle.SelectedIndex = m_Settings.StyledPlaylist ? 1 : 0;

        chkTray_Changed(null, null);

        //lstTracks.SetColumnsInfo(m_Settings.TracksListView);
        lstPlaylist.SetColumnsInfo(m_Settings.PlayListView);
      } else
        m_Settings = new Settings();
      m_LastfmScrobbler = new LastfmScrobbler(Utilities.DecryptString(m_Settings.ScrobblerSessionKey));

      if (m_Settings.WindowWidth > 0 && m_Settings.WindowHeight > 0){
        Width = m_Settings.WindowWidth;
        Height = m_Settings.WindowHeight;
      }
      if (m_Settings.WindowLeft >= 0 && m_Settings.WindowHeight >= 0){
        Left = m_Settings.WindowLeft;
        Top = m_Settings.WindowTop;
      }else
        WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner;
      if (m_Settings.WindowMaximized)
        WindowState = System.Windows.WindowState.Maximized;

      m_ArtDownloader = new ArtDownloader(m_Settings);
      m_ArtistArtDownloader = new ArtDownloader(m_Settings, 2);

      m_Mpc = new Mpc();
      m_Mpc.OnConnected += c => Dispatcher.BeginInvoke((MpcEventDelegate)MpcConnected, c);
      m_Mpc.OnDisconnected += c => Dispatcher.BeginInvoke((MpcEventDelegate)MpcDisconnected, c);

      m_MpcIdle = new Mpc();
      m_MpcIdle.OnConnected += c => Dispatcher.BeginInvoke((MpcEventDelegate)MpcIdleConnected, c);
      m_MpcIdle.OnSubsystemsChanged += MpcIdleSubsystemsChanged;

      DataContext = context = new Context(m_Mpc);
      context.Playlist = new ObservableCollection<MpdFile>();

      cmbSearch.SelectedIndex = 0;

      tabFileSystem.Visibility = m_Settings.ShowFilesystemTab ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
      playerControl.ShowStopButton = m_Settings.ShowStopButton;
      playerControl.Mpc = m_Mpc;
      lstPlaylist.Visibility = m_Settings.StyledPlaylist ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
      lstPlaylistStyled.Visibility = m_Settings.StyledPlaylist ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

      m_NotifyIcon = new System.Windows.Forms.NotifyIcon();
      m_NotifyIcon.Icon = new System.Drawing.Icon("mpd_icon.ico", new System.Drawing.Size(32,32));
      m_NotifyIcon.MouseDown += new System.Windows.Forms.MouseEventHandler(NotifyIcon_MouseDown);
      m_NotifyIconMenu = (ContextMenu)this.FindResource("TrayIconContextMenu");
      Closing += CloseHandler;

      if (!string.IsNullOrEmpty(m_Settings.ServerAddress)){
        m_StartTimer = new Timer();
        m_StartTimer.Interval = 500;
        m_StartTimer.Elapsed += StartTimerHandler;
        m_StartTimer.Start();
      }

      m_Updater = new Updater(new Uri("http://www.sakya.it/updater/updater.php"), "WpfMpdClient", "Windows");
      m_Updater.AppCurrentVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();
      m_Updater.CheckCompletedDelegate += CheckCompleted;
      m_Updater.Check();

      lstArtist.ItemsSource = m_ArtistsSource;
      Type t = typeof(ListboxEntry);
      lstArtist.SearchProperty = t.GetProperty("Artist");

      lstAlbums.ItemsSource = new MpdFile[0];
      lstAlbums.SearchProperty = t.GetProperty("Album");

      lstGenresAlbums.ItemsSource = m_GenresAlbumsSource;
      lstGenresAlbums.SearchProperty = t.GetProperty("Album");

      lstChannels.ItemsSource = m_Channels;
      cmbChannnels.ItemsSource = m_Channels;
      lstMessages.ItemsSource = m_Messages;
      CollectionView view = (CollectionView)CollectionViewSource.GetDefaultView(lstMessages.ItemsSource);
      PropertyGroupDescription group = new PropertyGroupDescription("Channel");
      view.GroupDescriptions.Add(group);

      m_ArtDownloader.Start();      
      txtStatus.Text = "Not connected";
      context.View = false;
      lstArtist.Focus();
    }

    public int CurrentTrackId
    {
      get { return (int)GetValue(CurrentTrackIdProperty); }
      set { SetValue(CurrentTrackIdProperty, value); }
    }

    public static readonly DependencyProperty CurrentTrackIdProperty = DependencyProperty.Register(
        "CurrentTrackId", typeof(int), typeof(MainWindow), new PropertyMetadata(0, null));

    private void MpcIdleConnected(Mpc connection)
    {
      if (!string.IsNullOrEmpty(m_Settings.Password)){
        if (!m_MpcIdle.Password(m_Settings.Password))
          return;
      }

      MpcIdleSubsystemsChanged(m_MpcIdle, Mpc.Subsystems.All);

      Mpc.Subsystems subsystems = Mpc.Subsystems.player | Mpc.Subsystems.playlist | Mpc.Subsystems.stored_playlist | Mpc.Subsystems.update |
                                  Mpc.Subsystems.mixer | Mpc.Subsystems.options;
      if (m_Mpc.Commands().Contains("channels"))
        subsystems |= Mpc.Subsystems.message | Mpc.Subsystems.subscription;
      m_MpcIdle.Idle(subsystems);
    }

    private void MpcIdleSubsystemsChanged(Mpc connection, Mpc.Subsystems subsystems)
    {
      if (m_Mpc == null || !m_Mpc.Connected)
        return;

      MpdStatus status = null;
      try{
        status = m_Mpc.Status();
      }catch{
        return;
      }
      if ((subsystems & Mpc.Subsystems.player) != 0 || (subsystems & Mpc.Subsystems.mixer) != 0 ||
          (subsystems & Mpc.Subsystems.options) != 0){
        Dispatcher.BeginInvoke(new Action(() =>
        {
          MenuItem m = m_NotifyIconMenu.Items[1] as MenuItem;
          m.Visibility = status.State != MpdState.Play ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
          m = m_NotifyIconMenu.Items[2] as MenuItem;
          m.Visibility = status.State == MpdState.Play ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

          MpdFile file = m_Mpc.CurrentSong();
          if (file != null)
          {
            ListboxEntry album;
            if (Albums.TryGetValue(file.Artist + ":" + file.Album, out album))
              file.AlbumEntry = album;
            else
            {
              file.AlbumEntry = album = new ListboxEntry();
              album.Info = new ObservableCollection<object>();
              FindInfo(album.Info, dir.Replace(file.File, "$1"), Dispatcher);
            }
          }
          playerControl.Update(status, file);          
          if (m_MiniPlayer != null)
            m_MiniPlayer.Update(status, file);

          if (m_CurrentTrack == null || file == null || m_CurrentTrack.Id != file.Id) {
            TrackChanged(file);
            m_CurrentTrack = file;
            CurrentTrackId = file != null ? file.Id : 0;
            m_CurrentTrackStart = DateTime.Now;
          }
        }));
      }

      if ((subsystems & Mpc.Subsystems.playlist) != 0){
        Dispatcher.BeginInvoke(new Action(() =>
        {
          PopulatePlaylist();
        }));
      }

      if ((subsystems & Mpc.Subsystems.update) != 0){
        int lastUpdate = m_LastStatus != null ? m_LastStatus.UpdatingDb : -1;
        Dispatcher.BeginInvoke(new Action(() =>
        {
          btnUpdate.IsEnabled = status.UpdatingDb <= 0;
          // Update db finished:
          if (lastUpdate > 0 && status.UpdatingDb <= 0)
              UpdateDbFinished();
        }));
      }

      if ((subsystems & Mpc.Subsystems.subscription) != 0)
        PopulateChannels();
      if ((subsystems & Mpc.Subsystems.message) != 0)
        PopulateMessages();

      m_LastStatus = status;
    }

    private void MpcConnected(Mpc connection)
    {
      if (!string.IsNullOrEmpty(m_Settings.Password)){
        if (!m_Mpc.Password(m_Settings.Password)){
          MessageBox.Show("Error connecting to server:\r\nWrong password", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
          return;
        }
      }

      List<string> commands = m_Mpc.Commands();
      if (!commands.Contains("channels"))
        tabMessages.Visibility = System.Windows.Visibility.Collapsed;

      txtStatus.Text = string.Format("Connected to {0}:{1} [MPD v.{2}]", m_Settings.ServerAddress, m_Settings.ServerPort, m_Mpc.Connection.Version);
      MpdStatistics stats = m_Mpc.Stats();      
      PopulateGenres();
      PopulatePlaylists();
      PopulateFileSystemTree();
      PopulatePlaylist();
      PopulateArtists();      
    }

    private void MpcDisconnected(Mpc connection)
    {
      if (!connection.Connected){
        txtStatus.Text = "Not connected";
        if (!m_IgnoreDisconnect  && m_Settings.AutoReconnect && m_ReconnectTimer == null){
          m_ReconnectTimer = new Timer();
          m_ReconnectTimer.Interval = m_Settings.AutoReconnectDelay * 1000;
          m_ReconnectTimer.Elapsed += ReconnectTimerHandler;
          m_ReconnectTimer.Start();
        }
      }
    }

    private void Connect(System.Windows.Threading.Dispatcher Dispatcher)
    {
      if (!string.IsNullOrEmpty(m_Settings.ServerAddress)) {
        Dispatcher.BeginInvoke((System.Action)(() => {
          txtStatus.Text = string.Format("Connecting to {0}:{1}...", m_Settings.ServerAddress, m_Settings.ServerPort);
        }));
        try {
          IPAddress[] addresses = System.Net.Dns.GetHostAddresses(m_Settings.ServerAddress);
          if (addresses.Length > 0) {
            IPAddress ip = addresses[0];
            IPEndPoint ie = new IPEndPoint(ip, m_Settings.ServerPort);

            m_IgnoreDisconnect = true;
            if (m_Mpc.Connected)
              m_Mpc.Connection.Disconnect();
            m_Mpc.Connection = new MpcConnection(ie);
            if (m_MpcIdle.Connected)
              m_MpcIdle.Connection.Disconnect();
            m_MpcIdle.Connection = new MpcConnection(ie);
            m_IgnoreDisconnect = false;
          }
        }
        catch (Exception ex) {
          Dispatcher.BeginInvoke((System.Action)(() => {
            txtStatus.Text = string.Empty;
          }));
        }
      }
    } // Connect

    private void PopulateArtists()
    {
      if (m_Mpc == null || !m_Mpc.Connected)
        return;

      m_ArtistsSource.Clear();
      List<string> artists = null;
      try{
        artists = m_Mpc.List(ScopeSpecifier.Artist);
      }catch (Exception ex){
        ShowException(ex);
        return;
      }

      artists.Sort();
      for (int i = 0; i < artists.Count; i++) {
        if (string.IsNullOrEmpty(artists[i]))
          artists[i] = Mpc.NoArtist;
        ListboxEntry entry = new ListboxEntry() { Type = ListboxEntry.EntryType.Artist, 
                                                  Artist = artists[i] };
        m_ArtistsSource.Add(entry);
      }
      if (artists.Count > 0){
        lstArtist.SelectedIndex = 0;
        lstArtist.ScrollIntoView(m_ArtistsSource[0]);
      }
    }

    private void PopulateGenres()
    {
      if (m_Mpc == null || !m_Mpc.Connected)
        return;

      List<string> genres = null;
      try{
        genres = m_Mpc.List(ScopeSpecifier.Genre);
      }catch (Exception ex){
        ShowException(ex);
        return;
      }

      genres.Sort();
      for (int i = 0; i < genres.Count; i++) {
        if (string.IsNullOrEmpty(genres[i]))
          genres[i] = Mpc.NoGenre;
      }
      lstGenres.ItemsSource = genres;
      if (genres.Count > 0){
        lstGenres.SelectedIndex = 0;
        lstGenres.ScrollIntoView(genres[0]);
      }
    }

    private void PopulatePlaylists()
    {
      if (m_Mpc == null || !m_Mpc.Connected)
        return;

      List<string> playlists = null;
      try{
        playlists = m_Mpc.ListPlaylists();
      }catch (Exception ex){
        ShowException(ex);
        return;
      }
      playlists.Sort();
      lstPlaylists.ItemsSource = playlists;
      if (playlists.Count > 0){
        lstPlaylists.SelectedIndex = 0;
        lstPlaylists.ScrollIntoView(playlists[0]);
      }
    }

    private void PopulateFileSystemTree()
    {
      if (m_Mpc == null || !m_Mpc.Connected)
        return;

      treeFileSystem.Items.Clear();
      if (!m_Settings.ShowFilesystemTab)
        return;

      TreeViewItem root = new TreeViewItem();
      root.Header = "Root";
      root.Tag = null;
      treeFileSystem.Items.Add(root);

      PopulateFileSystemTree(root.Items, null);
      if (treeFileSystem.Items != null && treeFileSystem.Items.Count > 0) {
        TreeViewItem item = treeFileSystem.Items[0] as TreeViewItem;
        item.IsSelected = true;
        item.IsExpanded = true;
      }
    }

    private void PopulateFileSystemTree(ItemCollection items, string path)
    {
      items.Clear();
      MpdDirectoryListing list = null;
      try{
        list = m_Mpc.LsInfo(path);
      }catch (Exception ex){
        ShowException(ex);
        return;
      }
      foreach (string dir in list.DirectoryList){
        TreeViewItem item = new TreeViewItem();
        item.Header = path != null ? dir.Remove(0, path.Length + 1) : dir;
        item.Tag = dir;
        if (HasSubdirectories(item.Tag.ToString())){
          item.Items.Add(null);
          item.Expanded += TreeItemExpanded;
        }
        items.Add(item);
      }
    }

    private bool HasSubdirectories(string path)
    {
      MpdDirectoryListing list = m_Mpc.LsInfo(path);
      return list.DirectoryList.Count > 0;      
    }

    private void TreeItemExpanded(object sender, RoutedEventArgs e)
    {
      TreeViewItem treeItem = sender as TreeViewItem;
      if (treeItem != null){
        if (treeItem.Items.Count == 1 && treeItem.Items[0] == null) {
          treeFileSystem.Cursor = Cursors.Wait;
          PopulateFileSystemTree(treeItem.Items, treeItem.Tag.ToString());
          treeFileSystem.Cursor = Cursors.Arrow;
        }
      }
    }

    private void treeFileSystem_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
      TreeViewItem treeItem = treeFileSystem.SelectedItem as TreeViewItem;
      if (treeItem != null){
        MpdDirectoryListing list = null;
        try{
          list = m_Mpc.LsInfo(treeItem.Tag != null ? treeItem.Tag.ToString() : null);
        }catch (Exception ex){
          ShowException(ex);
          return;
        }
        m_Tracks = new List<MpdFile>();
        foreach (MpdFile file in list.FileList)
          m_Tracks.Add(file);
        lstTracks.ItemsSource = m_Tracks;
        ScrollTracksToLeft();
      }
    }

    static readonly System.Text.RegularExpressions.Regex recording = new System.Text.RegularExpressions.Regex(@"^(.+?)(?i: music| works?| pieces?)?(?:, +[IVXivx\d/:-]*[IVXivx/:-][IVXivxa\d/:-]*\b|,? *\b(?:[Vv]ol\.?|No\.?|[Oo]p(?:\.|us)?|Book|BB|Sz\.?) +[IVXivxa\d/:-]+(.*?))*(?:,? +[(]([^()]+)[)])*$", System.Text.RegularExpressions.RegexOptions.Compiled);
    static readonly System.Text.RegularExpressions.Regex punct = new System.Text.RegularExpressions.Regex(@"(?:\W*[(][^()]+[)])*\W+");
    static readonly System.Text.RegularExpressions.Regex numbers = new System.Text.RegularExpressions.Regex(@"\b(?:(0|zero|zero)|(1|one|un)|(2|two|deux)|(3|three|trois)|(4|four|quatre)|(5|five|cinq)|(6|six|six)|(7|seven|sept)|(8|eight|huit)|(9|nine|neuf)|(10|ten|dix)|(11|eleven|onze)|(12|twelve|douze)|(13|thirteen|treize)|(14|fourteen|quatorze)|(15|fifteen|quinze)|(16|sixteen|seize)|(17|seventeen|dix-sept)|(18|eighteen|dix-huit)|(19|nineteen|dix-neuf)|(?:(20|twenty|vingt)|(30|thirty|trente)|(40|fourty|quarante)|(50|fifty|cinquante)|(60|sixty|soixante)|(70|seventy|soixante-dix)|(80|eighty|quatre-vingts)|(90|ninety|quatre-vingt-dix))(?: (?:(0|zero|zero)|(1|one|(?:et )?un)|(2|two|deux)|(3|three|trois)|(4|four|quatre)|(5|five|cinq)|(6|six|six)|(7|seven|sept)|(8|eight|huit)|(9|nine|neuf)|(10|ten|dix)|(11|eleven|onze)))?|(\d+))\b".Replace(" ", @"\W+"), System.Text.RegularExpressions.RegexOptions.Compiled);
    private void lstArtist_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      if (m_Mpc == null || !m_Mpc.Connected)
        return;

      if (lstArtist.SelectedItem == null) {
        lstAlbums.ItemsSource = new MpdFile[0];
        return;
      }

      var a = lstArtist.Selected();
      ArtistLabel.DataContext = a;
      if (a != null)
        if (a.Albums != null)
          lstAlbums_Show(a.Albums);
        else {
        string artist = a.Artist();
       Action<Action<Action>> populate = call => {
        IEnumerable<string> albums = null;
        try{
          albums = m_Mpc.List(ScopeSpecifier.Album, ScopeSpecifier.Artist, artist);
        }catch (Exception ex){
          ShowException(ex);
          return;
        }
        ListboxEntry last = null;
        if (a.Artist == "Misc")
          albums = albums.Where(_album => _album != "Misc");
        a.Albums = albums.Select(_album => {
          var album = string.IsNullOrEmpty(_album) ? Mpc.NoAlbum : _album;
          var display = recording.Replace(album, "$1$2");
          var grouping = numbers.Replace(punct.Replace(display, " ").ToLower(), x =>
            numbers.GetGroupNumbers().Select(i =>
              i > 0 && x.Groups[i].Success ? (i < 22 ? i - 1 : i < 29 ? 20 + (i - 21) * 20 : i < 41? i - 29 : int.Parse(x.Groups[i].Value)) : 0
            ).Sum().ToString("ZZ000")
          );
          return new ListboxEntry()
          {
            Type = ListboxEntry.EntryType.Album,
            Artist = artist,
            Album = album,
            Display = display,
            Grouping = grouping,
          };
        })
        .OrderBy(entry => entry.Grouping).ThenBy(entry => entry.Album)
        .Select(entry => {
          if (last != null && entry.Grouping == last.Grouping)
          {
            var rel = last.Related ?? new ObservableCollection<ListboxEntry>();
            if (rel.Count == 0)
                rel.Add(last);
            rel.Add(entry);
            entry.Related = last.Related = rel;
            entry.Head = false;
          }
          last = entry;
          return entry;
        }).ToList();
        call(() => lstAlbums_Show(a.Albums));
       };
       if (navigating)
         populate(x => x());
       else
         populate.BeginInvoke((Action<Action>)(x => Dispatcher.BeginInvoke(x)), populate.EndInvoke, null);

        m_ArtistArtDownloader.Soon(a);
      }
    }

    void lstAlbums_Show(IList<ListboxEntry> source)
    {
      lstAlbums.ItemsSource = source;
      if (source.Count > 0)
      {
        lstAlbums.SelectedIndex = 0;
        lstAlbums.ScrollIntoView(source[0]);
      }
    }

    private void lstGenres_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      if (m_Mpc == null || !m_Mpc.Connected)
        return;

      if (lstGenres.SelectedItem == null) {
        m_GenresAlbumsSource.Clear();
        return;
      }

      m_GenresAlbumsSource.Clear();
      string genre = lstGenres.SelectedItem.ToString();
      if (genre == Mpc.NoGenre)
        genre = string.Empty;

      List<MpdFile> files = null;
      try{
        files = m_Mpc.Find(ScopeSpecifier.Genre, genre);
      }catch (Exception ex){
        ShowException(ex);
        return;
      }
      files.Sort(delegate(MpdFile p1, MpdFile p2)
                 { 
                   return string.Compare(p1.Album, p2.Album);
                 });
      MpdFile lastFile = null;
      MpdFile last = files.Count > 0 ? files[files.Count - 1] : null;
      foreach (MpdFile file in files){
        if (lastFile != null && lastFile.Album != file.Album || file == last){
          string album = file == last ? file.Album : lastFile.Album;
          if (string.IsNullOrEmpty(album))
            album = Mpc.NoAlbum;
          ListboxEntry entry = new ListboxEntry()
          {
            Type = ListboxEntry.EntryType.Album,
            Artist = file == last ? file.Artist : lastFile.Artist,
            Album = album
          };
          m_GenresAlbumsSource.Add(entry);
        }
        lastFile = file;
      }

      if (m_GenresAlbumsSource.Count > 0) {
        lstGenresAlbums.SelectedIndex = 0;
        lstGenresAlbums.ScrollIntoView(m_GenresAlbumsSource[0]);
      }
    }

    private void lstPlaylists_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      if (m_Mpc == null || !m_Mpc.Connected)
        return;

      ListBox list = sender as ListBox;
      if (list.SelectedItem != null) {
        string playlist = list.SelectedItem.ToString();
        
        try{
          m_Tracks = m_Mpc.ListPlaylistInfo(playlist);
        }catch (Exception ex){
          ShowException(ex);
          return;
        }
        lstTracks.ItemsSource = m_Tracks;
        ScrollTracksToLeft();
      } else {
        m_Tracks = null;
        lstTracks.ItemsSource = null;
      }
    } 

    private void lstTracks_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {

    }

    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ListboxEntry> Albums = new System.Collections.Concurrent.ConcurrentDictionary<string, ListboxEntry>();

    static readonly System.Text.RegularExpressions.Regex dir = new System.Text.RegularExpressions.Regex(@"^(.*)/(?:\\/|[^/]*)$");
    private void lstAlbums_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      if (m_Mpc == null || !m_Mpc.Connected)
        return;

      ListBox list = sender as ListBox;
      if (list.SelectedItem != null) {
        Dictionary<ScopeSpecifier, string> search = new Dictionary<ScopeSpecifier, string>();

        ListBox listBox = null;
        if (tabBrowse.SelectedIndex == 0 && lstArtist.SelectedItem != null) {
          string artist = lstArtist.Selected().Artist();
          search[ScopeSpecifier.Artist] = artist;
          listBox = lstAlbums;
        } else if (tabBrowse.SelectedIndex == 1 && lstGenres.SelectedItem != null) {
          string genre = lstGenres.SelectedItem.ToString();
          if (genre == Mpc.NoGenre)
            genre = string.Empty;
          search[ScopeSpecifier.Genre] = genre;
          listBox = lstGenresAlbums;
        }

        var album = listBox.Selected();
        search[ScopeSpecifier.Album] = album.Album();

       Action<Action<Action>> populate = call => {
        if (album != null) {
          album.Related.Do(r => r.Selected = true);
          
          if (e != null) {
            var old = e.RemovedItems.OfType<ListboxEntry>().FirstOrDefault();
            if (old != null && !(album.Related != null && album.Related.Contains(old)))
              old.Related.Do(r => r.Selected = false);
          }

          call(() => {
              listBox.ScrollIntoView(album);
          });
        }

        try {
          m_Tracks = m_Mpc.Find(search);
          if (album != null) {
            Albums[album.Artist + ":" + album.Album] = album;
            album.Tracks = () => m_Tracks;
            if (album.Info == null)
              album.Info = new ObservableCollection<object>();
            m_ArtDownloader.Now(album);
          }
          m_Tracks.Do(m => m.AlbumEntry = album);
          var selection = m_Tracks.ToDictionary(m => m.File);
          var all = m_Tracks
            .OrderBy(m => m.Disc * 1000 + m.TrackNo)
            .GroupBy(m => dir.Replace(m.File, "$1"))
            .Where(g => {
              if (album != null && album.Album != "Misc")
                FindInfo(album.Info, g.Key, Dispatcher);
              return true;
            })
            .SelectMany(Utilities.Try<IGrouping<string, MpdFile>, IEnumerable<MpdFile>>(g =>
              m_Mpc.ListAllInfo(g.Key)
                .OrderBy(m => m.Disc * 1000 + m.TrackNo)
                .Where(m => (m.Supplement = !selection.Remove(m.File)) || true)
            ))
            .ToList();
          all.GroupBy(m => m.Artist).Do(a =>
            a.GroupBy(m => m.Album).Do(b => {
            var i = new ListboxEntry() {
              Type = ListboxEntry.EntryType.Album,
              Artist = a.Key,
              Album = b.Key,
              Tracks = () => b,
            };
            b.Do(m => m.AlbumEntry = i);
            m_ArtDownloader.Soon(i);
          }));
          all.AddRange(selection.Values);
          call(() => {
            if (album != null)
              lstInfo.ItemsSource = album.Info;
            lstTracks.ItemsSource = all;
            ScrollTracksToLeft();
          });
        }
        catch (Exception ex)
        {
            ShowException(ex);
            return;
        }
       };
       if (navigating)
         populate(x => x());
       else
         populate.BeginInvoke((Action<Action>)(x => Dispatcher.BeginInvoke(x)), populate.EndInvoke, null);
      }
      else
      {
        m_Tracks = null;
        lstTracks.ItemsSource = null;
      }
    }

    public static void FindInfo(IList<object> info, string dir, System.Windows.Threading.Dispatcher Dispatcher)
    {
      if (info != null && info.Count == 0)
        ArtDownloader.Listing(dir,
          x => x.EndsWith("pdf") || x.EndsWith("html") || x.EndsWith("txt"),
          us => (Dispatcher).BeginInvoke((System.Action)(() => {
            us.GroupBy(u => Path.GetFileNameWithoutExtension(u.LocalPath)).Do(x => {
              var u = x.FirstOrDefault(y => y.LocalPath.ToLower().Contains("pdf")) ?? x.FirstOrDefault();
              var p = (u.Segments.LastOrDefault() ?? "").ToLower();
              info.Add(new {
                Kind =
                p.Contains("booklet") ? "Booklet" :
                p.Contains("pdf") ? "PDF" :
                "Information",
                Label = x.Key,
                Uri = u,
              });
            });
          })));
    }

    private void btnApplySettings_Click(object sender, RoutedEventArgs e)
    {
      m_Settings.ServerAddress = txtServerAddress.Text;
      int port = 0;
      if (int.TryParse(txtServerPort.Text, out port))
        m_Settings.ServerPort = port;
      else
        m_Settings.ServerPort = 6600;
      m_Settings.Password = txtPassword.Password;
      m_Settings.AutoReconnect = chkAutoreconnect.IsChecked == true;
      m_Settings.AutoReconnectDelay = 10;
      m_Settings.ShowStopButton = chkShowStop.IsChecked == true;
      m_Settings.ShowFilesystemTab = chkShowFilesystem.IsChecked == true;
      m_Settings.MinimizeToTray = chkMinimizeToTray.IsChecked == true;
      m_Settings.CloseToTray = chkCloseToTray.IsChecked == true;
      m_Settings.ShowMiniPlayer = chkShowMiniPlayer.IsChecked == true;
      m_Settings.Scrobbler = chkScrobbler.IsChecked == true;
      m_Settings.InfoLanguage = cmbLastFmLang.SelectedIndex < 0 ? m_Languages[0] : m_Languages[cmbLastFmLang.SelectedIndex];
      m_Settings.StyledPlaylist = cmbPlaylistStyle.SelectedIndex == 1;

      m_Settings.Serialize(Settings.GetSettingsFileName());

      playerControl.ShowStopButton = m_Settings.ShowStopButton;
      tabFileSystem.Visibility = m_Settings.ShowFilesystemTab ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

      lstPlaylist.Visibility = m_Settings.StyledPlaylist ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
      lstPlaylistStyled.Visibility = m_Settings.StyledPlaylist ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

      m_IgnoreDisconnect = true;
      if (m_Mpc.Connected)
        m_Mpc.Connection.Disconnect();
      if (m_MpcIdle.Connected)
        m_MpcIdle.Connection.Disconnect();

      Connect();
      tabBrowse.SelectedIndex = 0;
      m_IgnoreDisconnect = false;
    }

    void Connect()
    {
        ((System.Action<System.Windows.Threading.Dispatcher>)Connect).BeginInvoke(Dispatcher, a => { }, null);
    }

    private void ReconnectTimerHandler(object sender, ElapsedEventArgs e)
    {
      m_ReconnectTimer.Stop();
      m_ReconnectTimer = null;
      Connect(Dispatcher);
    } // ReconnectTimerHandler

    private void StartTimerHandler(object sender, ElapsedEventArgs e)
    {
      if (m_StartTimer != null)
        m_StartTimer.Stop();
      m_StartTimer = null;
      Connect(Dispatcher);
    } // StartTimerHandler

    private void PopulatePlaylist()
    {
      if (m_Mpc == null || !m_Mpc.Connected)
        return;

      List<MpdFile> tracks = null;
      try{
        tracks = m_Mpc.PlaylistInfo();
      }catch (Exception ex){
        ShowException(ex);
        return;
      }
      context.Playlist.Clear();
      foreach (MpdFile file in tracks)
          context.Playlist.Add(file);
    }

    private void ContextMenu_Click(object sender, RoutedEventArgs args)
    {
      if (m_Mpc == null || !m_Mpc.Connected)
        return;

      int last = -1;
      try {
        last = m_Mpc.PlaylistInfo().Last().Pos;
      } catch {
      }

      MenuItem item = sender as MenuItem;
      if (item.Name == "mnuDeletePlaylist") {
        string playlist = lstPlaylists.SelectedItem.ToString();
        if (Utilities.Confirm("Delete", string.Format("Delete playlist \"{0}\"?", playlist))){
          try{
            m_Mpc.Rm(playlist);
            PopulatePlaylists();
          }catch (Exception ex){
            ShowException(ex);
            return;
          }
        }
        return;
      }

      if (item.Name == "mnuAddReplace" || item.Name == "mnuAddReplacePlay"){
        try{
          m_Mpc.Clear();
        }catch (Exception ex){
          ShowException(ex);
          return;
        }
        if (lstPlaylist.Items.Count > 0)
          lstPlaylist.ScrollIntoView(lstPlaylist.Items[0]);
      }

      if (m_Tracks != null){
        foreach (MpdFile f in m_Tracks){
          try{
            m_Mpc.Add(f.File);
          }catch (Exception){}
        }
      }        
      if (item.Name == "mnuAddReplacePlay"){
        try{
          m_Mpc.Play();
        }catch (Exception ex){
          ShowException(ex);
          return;
        }
      }        
      if (item.Name == "mnuAddPlay"){
        try{
          if (last < 0)
            m_Mpc.Play();
          else
            m_Mpc.Play(last + 1);
        }catch (Exception ex){
          ShowException(ex);
          return;
        }
      }        
}

    private void TracksContextMenu_Click(object sender, RoutedEventArgs args)
    {
      if (m_Mpc == null || !m_Mpc.Connected)
        return;

      int last = -1;
      try {
        last = m_Mpc.PlaylistInfo().Last().Pos;
      } catch {
      }

      MenuItem mnuItem = sender as MenuItem;
      if (mnuItem.Name == "mnuAddReplace" || mnuItem.Name == "mnuAddReplacePlay"){
        try{
          m_Mpc.Clear();
        }catch (Exception ex){
          ShowException(ex);
          return;
        }
        if (lstPlaylist.Items.Count > 0)
          lstPlaylist.ScrollIntoView(lstPlaylist.Items[0]);
      }

      foreach (MpdFile file  in lstTracks.SelectedItems)
        m_Mpc.Add(file.File);

      if (mnuItem.Name == "mnuAddReplacePlay")
        m_Mpc.Play();
      if (mnuItem.Name == "mnuAddPlay"){
        try{
          if (last < 0)
            m_Mpc.Play();
          else
            m_Mpc.Play(last + 1);
        }catch (Exception ex){
          ShowException(ex);
          return;
        }
      }
    }


    private void btnUpdate_Click(object sender, RoutedEventArgs e)
    {
      if (m_Mpc.Connected){
        try{
          m_Mpc.Update();
        }catch (Exception ex){
          ShowException(ex);
          return;
        }
      }
    }

    private void tabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      if (m_Mpc == null || !m_Mpc.Connected)
        return;

      if (e.AddedItems.Count > 0) {
        TabItem tab = e.AddedItems[0] as TabItem;
        if (tab == null)
          return;
      } else
        return;

      if (tabControl.SelectedIndex == 1){

      }else if (tabControl.SelectedIndex == 2){
        Dispatcher.BeginInvoke(new Action(() =>
        {
          StringBuilder sb = new StringBuilder();
          sb.AppendLine(m_Mpc.Stats().ToString());
          sb.AppendLine(m_Mpc.Status().ToString());
          txtServerStatus.Text = sb.ToString();
        }));
      } else if (tabControl.SelectedIndex == 3) {
        PopulateChannels();
        //PopulateMessages();
      }
    }

    private void tabBrowse_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      if (m_Mpc == null || !m_Mpc.Connected)
        return;

      if (e.AddedItems.Count > 0) {
        TabItem tab = e.AddedItems[0] as TabItem;
        if (tab == null)
          return;
      } else
        return;

      if (tabBrowse.SelectedIndex == 0)
        lstAlbums_SelectionChanged(lstAlbums, null);
      else if (tabBrowse.SelectedIndex == 1)
        lstAlbums_SelectionChanged(lstGenresAlbums, null);
      else if (tabBrowse.SelectedIndex == 2)
        lstPlaylists_SelectionChanged(lstPlaylists, null);
      else if (tabBrowse.SelectedIndex == 3)
        treeFileSystem_SelectedItemChanged(null, null);
      else if (tabBrowse.SelectedIndex == 4)
        btnSearch_Click(null, null);
    }

    private void lstPlaylist_Selected(object sender, RoutedEventArgs e)
    {
      ListBoxItem item = sender as ListBoxItem;
      //if (item != null)
        //item.IsSelected = false;
    }

    private void lstPlaylist_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
      if (m_Mpc == null || !m_Mpc.Connected)
        return;

      ListBoxItem item = sender as ListBoxItem;
      if (item != null) {
        MpdFile file = item.DataContext as MpdFile;
        if (file != null) {
          try{
            m_Mpc.Play(file.Pos);
          }catch (Exception ex){
            ShowException(ex);
            return;
          }
        }
      }
    }

    private void lstPlaylist_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
      if (m_Mpc == null || !m_Mpc.Connected)
        return;

      ListBoxItem item = sender as ListBoxItem;
      if (item != null) {
        MpdFile file = item.DataContext as MpdFile;
        if (file != null) {
          try{
            NavigateTo(file);
            e.Handled = true;
          }catch (Exception ex){
            ShowException(ex);
            return;
          }
        }
      }
    }

    bool Select(ListBox list, object item)
    {
      try
      {
        if (item == null)
          return false;
        list.SelectedItem = item;
        list.ScrollIntoView(item);
        return true;
      }
      catch
      {
        return false;
      }
    }

    bool navigating = false;
    void NavigateTo(MpdFile file)
    {
      try
      {
        navigating = true;
        if ((lstArtist.SelectedItems.OfType<ListboxEntry>().Any(m => m.Artist == file.Artist)
          || Select(lstArtist, lstArtist.Items.OfType<ListboxEntry>().FirstOrDefault(m => m.Artist == file.Artist)))
          && Select(lstAlbums, lstAlbums.Items.OfType<ListboxEntry>().FirstOrDefault(m => m.Album == file.Album))
          && Select(lstTracks, lstTracks.Items.OfType<MpdFile>().FirstOrDefault(m => m.File == file.File)))
        {
          tabControl.SelectedIndex = 0;
          tabBrowse.SelectedIndex = 0;
          context.View = true;
        }
      } finally {
        navigating = false;
      }
    }

    private void lstTracks_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
      if (m_Mpc == null || !m_Mpc.Connected)
        return;

      var view = sender as ListView;
      if (view != null)
      {
        MpdFile file = view.SelectedItems.OfType<MpdFile>().FirstOrDefault();
        if (file != null)
        {
          try
          {
            if (file.Supplement)
              NavigateTo(file);
            else
              m_Mpc.Add(file.File);
          }
          catch (Exception ex)
          {
            ShowException(ex);
            return;
          }
        }
      }
    }

    private void hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
      m_About.hyperlink_RequestNavigate(sender, e);
    }

    public static string MpdVersion()
    {
      if (This != null && This.m_Mpc != null && This.m_Mpc.Connected)
        return This.m_Mpc.Connection.Version;
      return string.Empty;
    }

    public static void Stop()
    {
      if (This.m_Mpc.Connected) {
        switch (This.m_Mpc.Status().State) {
          case MpdState.Play:
          case MpdState.Pause:
            try{
              This.m_Mpc.Stop();
            }catch (Exception ex){
              This.ShowException(ex);
              return;
            }
            break;
        }
      }
    }

    public static void PlayPause()
    {
      if (This.m_Mpc.Connected){
        switch (This.m_Mpc.Status().State){
          case MpdState.Play:
            This.mnuPause_Click(null, null);
            break;
          case MpdState.Pause:
          case MpdState.Stop:
            This.mnuPlay_Click(null, null);
            break;
        }
      }
    }

    public static void NextTrack()
    {
      This.mnuNext_Click(null, null);
    }

    public static void PreviousTrack()
    {
      This.mnuPrevious_Click(null, null);
    }

    private void btnClear_Click(object sender, RoutedEventArgs e)
    {
      if (m_Mpc.Connected){
        try{
          m_Mpc.Clear();
        }catch (Exception ex){
          ShowException(ex);
          return;
        }
      }
    }

    private void btnSave_Click(object sender, RoutedEventArgs e)
    {
      if (m_Mpc.Connected){
        try{
          m_Mpc.Save(txtPlaylist.Text);
        }catch (Exception ex){
          ShowException(ex);
          return;
        }
        txtPlaylist.Clear();
        PopulatePlaylists();
      }
    }

    private void txtPlaylist_TextChanged(object sender, TextChangedEventArgs e)
    {
      btnSave.IsEnabled = !string.IsNullOrEmpty(txtPlaylist.Text);
    }

    private void CloseHandler(object sender, CancelEventArgs e)
    {
      if (!m_Close && m_Settings.CloseToTray){
        Hide();
        if (m_NotifyIcon != null && !m_Close){
          m_StoredWindowState = WindowState;
          m_NotifyIcon.BalloonTipText = "WpfMpdClient has been minimized. Click the tray icon to show.";
          m_NotifyIcon.BalloonTipTitle = "WpfMpdClient";

          m_NotifyIcon.Visible = true;
          m_NotifyIcon.ShowBalloonTip(2000);

          if (m_Settings.ShowMiniPlayer){
            if (m_MiniPlayer == null){
              m_MiniPlayer = new MiniPlayerWindow(m_Mpc, m_Settings);
              if (m_Settings.MiniWindowLeft >= 0 && m_Settings.MiniWindowTop >= 0){
                m_MiniPlayer.Left = m_Settings.MiniWindowLeft;
                m_MiniPlayer.Top = m_Settings.MiniWindowTop;
              }
              m_MiniPlayer.Update(m_LastStatus, m_CurrentTrack);
            }
            m_MiniPlayer.Show();
          }
        }
        e.Cancel = true;
      }

      if (m_Close){
        if (IsVisible)
          m_Settings.WindowMaximized = WindowState == System.Windows.WindowState.Maximized;
        else
          m_Settings.WindowMaximized = m_StoredWindowState == System.Windows.WindowState.Maximized;
        m_Settings.WindowLeft = Left;
        m_Settings.WindowTop = Top;
        m_Settings.WindowWidth = ActualWidth;
        m_Settings.WindowHeight = ActualHeight;
        //m_Settings.TracksListView = lstTracks.GetColumnsInfo();
        m_Settings.PlayListView = lstPlaylist.GetColumnsInfo();

        m_Settings.Serialize(Settings.GetSettingsFileName());

        m_LastfmScrobbler.SaveCache();

        DiskImageCache.DeleteCacheFiles();
      }
    } // CloseHandler

    private void Window_StateChanged(object sender, EventArgs e)
    {
      if (WindowState == System.Windows.WindowState.Minimized && m_Settings.MinimizeToTray) {
        Hide();
        if (m_NotifyIcon != null) {
          m_NotifyIcon.BalloonTipText = "WpfMpdClient has been minimized. Click the tray icon to show.";
          m_NotifyIcon.BalloonTipTitle = "WpfMpdClient";
          m_NotifyIcon.Visible = true;
          m_NotifyIcon.ShowBalloonTip(2000);
        }
      } else {
        m_StoredWindowState = WindowState;
      }
    } // Window_StateChanged

    private void NotifyIcon_MouseDown(object sender, System.Windows.Forms.MouseEventArgs e)
    {
      if (e.Button == System.Windows.Forms.MouseButtons.Right) {
        m_NotifyIconMenu.IsOpen = !m_NotifyIconMenu.IsOpen;
      }else if (e.Button == System.Windows.Forms.MouseButtons.Left){
        m_NotifyIconMenu.IsOpen = false;
        if (m_MiniPlayer != null){
          m_MiniPlayer.Close();
          m_MiniPlayer = null;
        }
        Show();
        WindowState = m_StoredWindowState;
        Activate();
        Focus();
        m_NotifyIcon.Visible = false;
      }
    } // NotifyIcon_MouseDown

    private void UpdateDbFinished()
    {
      PopulateArtists();
      PopulateGenres();
      PopulatePlaylists();
      PopulatePlaylist();
    } // UpdateDbFinished

    private void TrackChanged(MpdFile track)
    {
      if (track != null)
        Title = track.Title + ", " + track.Album + ", " + track.Artist;
      if (m_Settings.Scrobbler){
        if (m_CurrentTrack != null && m_CurrentTrack.Time >= 30){
          double played = (DateTime.Now - m_CurrentTrackStart).TotalSeconds;
          if (played >= 240 || played >= m_CurrentTrack.Time / 2) 
            m_LastfmScrobbler.Scrobble(m_CurrentTrack.Artist, m_CurrentTrack.Title, m_CurrentTrack.Album, m_CurrentTrackStart);
        }

        if (track != null){
          m_LastfmScrobbler.UpdateNowPlaying(track.Artist, track.Title, track.Album);
        }
      }

      System.Threading.ThreadPool.QueueUserWorkItem(new System.Threading.WaitCallback(GetLyrics), track);
      if (track != null && (m_CurrentTrack == null || m_CurrentTrack.Artist != track.Artist))
        System.Threading.ThreadPool.QueueUserWorkItem(new System.Threading.WaitCallback(GetArtistInfo), track.Artist);
      else if (track == null)
        System.Threading.ThreadPool.QueueUserWorkItem(new System.Threading.WaitCallback(GetArtistInfo), string.Empty);

      if (track != null && (m_CurrentTrack == null || m_CurrentTrack.Artist != track.Artist || m_CurrentTrack.Album != track.Album))
        System.Threading.ThreadPool.QueueUserWorkItem(new System.Threading.WaitCallback(GetAlbumInfo), new List<string>() { track.Artist, track.Album});
      else if (track == null)
        System.Threading.ThreadPool.QueueUserWorkItem(new System.Threading.WaitCallback(GetAlbumInfo), new List<string>() { string.Empty, string.Empty});


      if (m_NotifyIcon != null && track != null && (m_MiniPlayer == null || !m_MiniPlayer.IsVisible)) {
        string trackText = string.Format("\"{0}\"\r\n{1}", track.Title, track.Artist);
        if (trackText.Length >= 64)
          m_NotifyIcon.Text = string.Format("{0}...", trackText.Substring(0, 60));
        else
          m_NotifyIcon.Text = trackText;

        if (m_NotifyIcon.Visible){
          m_NotifyIcon.BalloonTipText = string.Format("\"{0}\"\r\n{1}\r\n{2}", track.Title, track.Album, track.Artist);
          m_NotifyIcon.BalloonTipTitle = "WpfMpdClient";
          m_NotifyIcon.ShowBalloonTip(2000);
        }
      }
    } // TrackChanged

    public void Quit()
    {      
      m_NotifyIcon.Visible = false;
      m_Close = true;
      Application.Current.Shutdown();
    }

    private void mnuQuit_Click(object sender, RoutedEventArgs e)
    {
      Quit();
    }

    private void mnuPrevious_Click(object sender, RoutedEventArgs e)
    {
      PreviousTrack();
    }

    private void mnuNext_Click(object sender, RoutedEventArgs e)
    {
      NextTrack();
    }

    private void mnuPlay_Click(object sender, RoutedEventArgs e)
    {
      if (m_Mpc.Connected){
        try{
          m_Mpc.Play();
        }catch (Exception ex){
          ShowException(ex);
          return;
        }
      }
    }

    private void mnuPause_Click(object sender, RoutedEventArgs e)
    {
      if (m_Mpc.Connected){
        try{
          m_Mpc.Pause(true);
        }catch (Exception ex){
          ShowException(ex);
          return;
        }
      }
    }

    private void GetArtistInfo(object state)
    {
      string artist = (string)state;
      Dispatcher.BeginInvoke(new Action(() =>
      {
        txtArtist.Text = !string.IsNullOrEmpty(artist) ? "Downloading info" : string.Empty;
      }));

      if (!string.IsNullOrEmpty(artist)) {
        string info = LastfmScrobbler.GetArtistInfo(m_Settings.InfoLanguage, artist);
        if (string.IsNullOrEmpty(info))
          info = "No info found";

        Dispatcher.BeginInvoke(new Action(() =>
        {
          Utilities.RenderHtml(txtArtist, info, hyperlink_RequestNavigate);
          scrArtist.ScrollToTop();
        }));
      }
    }

    private void GetAlbumInfo(object state)
    {
      List<string> values = state as List<string>;
      string artist = values[0];
      string album = values[1];
      Dispatcher.BeginInvoke(new Action(() =>
      {
        txtAlbum.Text = !string.IsNullOrEmpty(artist) && !string.IsNullOrEmpty(album) ? "Downloading info" : string.Empty;
      }));

      if (!string.IsNullOrEmpty(artist) && !string.IsNullOrEmpty(album)) {
        string info = LastfmScrobbler.GetAlbumInfo(artist, album, m_Settings.InfoLanguage);
        if (string.IsNullOrEmpty(info))
          info = "No info found";

        Dispatcher.BeginInvoke(new Action(() =>
        {
          Utilities.RenderHtml(txtAlbum, info, hyperlink_RequestNavigate);
          scrAlbum.ScrollToTop();
        }));
      }
    }

    private void GetLyrics(object state)
    {
      MpdFile track = state as MpdFile;
      Dispatcher.BeginInvoke(new Action(() =>
      {
        txtLyrics.Text = track != null ? "Downloading lyrics" : string.Empty;
      }));

      if (track == null)
        return;

      string lyrics = Utilities.GetLyrics(track.Artist, track.Title);
      if (string.IsNullOrEmpty(lyrics))
        lyrics = "No lyrics found";

      Dispatcher.BeginInvoke(new Action(() =>
      {
        txtLyrics.Text = lyrics;
        scrLyrics.ScrollToTop();
      }));
    }

    private void btnSearch_Click(object sender, RoutedEventArgs e)
    {
      if (m_Mpc == null || !m_Mpc.Connected)
        return;

      if (!string.IsNullOrEmpty(txtSearch.Text)){
        ScopeSpecifier searchBy = ScopeSpecifier.Title;
        switch (cmbSearch.SelectedIndex){
          case 0:
            searchBy = ScopeSpecifier.Artist;
            break;
          case 1:
            searchBy = ScopeSpecifier.Album;
            break;
          case 2:
            searchBy = ScopeSpecifier.Title;
            break;
        }
        try{
          m_Tracks = m_Mpc.Search(searchBy, txtSearch.Text);
        }catch (Exception ex){
          ShowException(ex);
          return;
        }
        lstTracks.ItemsSource = m_Tracks;
        ScrollTracksToLeft();
      }else{
        m_Tracks = null;
        lstTracks.ItemsSource = null;
      }
    }

    private void btnSearchClear_Click(object sender, RoutedEventArgs e)
    {
      txtSearch.Text = string.Empty;
      cmbSearch.SelectedIndex = 0;
      m_Tracks = null;
      lstTracks.ItemsSource = null;
    }

    private void CheckCompleted(UpdaterApp app)
    {
      m_App = app;
      Dispatcher.BeginInvoke(new Action(() =>
      {
        btnCheckUpdates.IsEnabled = true;
        if (m_App != null && m_App.Version > Assembly.GetExecutingAssembly().GetName().Version) {
          UpdateConfirmWindow cdlg = new UpdateConfirmWindow(m_App);
          cdlg.Owner = this;
          if (cdlg.ShowDialog() == true){
            UpdateWindow dlg = new UpdateWindow(m_Updater, m_App);
            dlg.Owner = this;
            dlg.ShowDialog();
          }
        }
      }));
    }

    private void btnCheckUpdates_Click(object sender, RoutedEventArgs e)
    {
      btnCheckUpdates.IsEnabled = false;
      m_Updater.Check();
    }

    private void btnScrobblerAuthorize_Click(object sender, RoutedEventArgs e)
    {
      string token = m_LastfmScrobbler.GetToken();
      string url = m_LastfmScrobbler.GetAuthorizationUrl(token);

      BrowserWindow dlg = new BrowserWindow();
      dlg.Owner = this;
      dlg.NavigateTo(url);
      dlg.ShowDialog();

      m_Settings.ScrobblerSessionKey = Utilities.EncryptString(m_LastfmScrobbler.GetSession());
      btnApplySettings_Click(null, null);
    }

    private void txtServerPort_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
      string chars = "0123456789";
      char keyChar = e.Text.ToCharArray().First();
      if (chars.IndexOf(keyChar) == -1 && keyChar != 8)
        e.Handled = true;
    }

    private void LisboxItem_Loaded(object sender, RoutedEventArgs e)
    {
      StackPanel stackPanel = sender as StackPanel;
      if (stackPanel != null){
        ListboxEntry entry = stackPanel.DataContext as ListboxEntry;
        if (entry != null) {
          //if (entry.Type == ListboxEntry.EntryType.Artist)
            //entry.Tracks = () => m_Mpc.List(ScopeSpecifier.Filename, ScopeSpecifier.Artist, entry.Artist).Select(f => new MpdFile(f));
          m_ArtDownloader.Soon(entry);
        }
      }
    }

    private void ScrollTracksToLeft()
    {
      ScrollViewer listViewScrollViewer = Utilities.GetVisualChild<ScrollViewer>(lstTracks);
      if (listViewScrollViewer != null)
        listViewScrollViewer.ScrollToLeftEnd();
    }

		private void dragMgr_ProcessDrop( object sender )
		{
      if (m_Mpc.Connected){
        try{
          //m_Mpc.Move(e.OldIndex, e.NewIndex);
        }catch (Exception ex){
          ShowException(ex);
          return;
        }
      }
    }

    private void chkTray_Changed(object sender, RoutedEventArgs e)
    {
      chkShowMiniPlayer.IsEnabled = chkCloseToTray.IsChecked == true || chkMinimizeToTray.IsChecked == true;
    }

    private void ShowException(Exception ex)
    {
      //MessageBox.Show(ex.ToString(), "Error", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    #region Client to client Messages
    private void PopulateChannels()
    {
      if (m_Mpc == null || !m_Mpc.Connected)
        return;

      List<string> channels = null;
      try{
        channels = m_Mpc.Channels();
      }catch (Exception ex){
        ShowException(ex);
        return;
      }
      List<MpdChannel> NewChannels = new List<MpdChannel>();
      foreach (string c in channels) {
        MpdChannel ch = GetChannel(c);
        NewChannels.Add(new MpdChannel() { Name = c, 
                                           Subscribed = ch != null ? ch.Subscribed : false });
      }

      m_Channels.Clear();
      foreach (MpdChannel c in NewChannels)
        m_Channels.Add(c);
    }

    private void PopulateMessages()
    {
      if (m_Mpc == null || !m_Mpc.Connected)
        return;

      List<MpdMessage> messages = null;
      try{
        messages = m_Mpc.ReadChannelsMessages();
      }catch (Exception ex){
        ShowException(ex);
        return;
      }
      foreach (MpdMessage m in messages)
        m_Messages.Add(m);
    }

    private void lstChannels_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {

    }

    private void btnSendMessage_Click(object sender, RoutedEventArgs e)
    {
      if (m_Mpc == null || !m_Mpc.Connected)
        return;

      string channel = cmbChannnels.Text;
      if (!string.IsNullOrEmpty(channel) && !string.IsNullOrEmpty(txtMessage.Text)) {
        channel = channel.Trim();
        try{
          m_Mpc.ChannelSubscribe(channel);
        }catch (Exception ex){
          ShowException(ex);
          return;
        }        
        if (m_Mpc.ChannelSendMessage(channel, txtMessage.Text)) {
          m_Messages.Add(new MpdMessage() { Channel = channel, Message = txtMessage.Text, DateTime = DateTime.Now });
          txtMessage.Clear();
          MpdChannel c = GetChannel(channel);
          if (c != null)
            c.Subscribed = true;
          else
            m_Channels.Add(new MpdChannel() { Name=channel, Subscribed=true });

          Expander exp = GetExpander(channel);
          if (exp != null)
            exp.IsExpanded = true;
        }      
      }
    }

    private Expander GetExpander(string name)
    {
      foreach (Expander e in m_MessagesExpanders) {
        if (e.Tag as string == name)
          return e;
      }
      return null;
    }

    private MpdChannel GetChannel(string name)
    {
      foreach (MpdChannel c in m_Channels) {
        if (c.Name == name)
          return c;
      }
      return null;       
    }

    private void Expander_Loaded(object sender, RoutedEventArgs e)
    {
      m_MessagesExpanders.Add(sender as Expander);
    }

    private void Expander_Unloaded(object sender, RoutedEventArgs e)
    {
      m_MessagesExpanders.Remove(sender as Expander);
    }

    private void ChannelItem_DoubleClick(object sender, MouseButtonEventArgs e)
    {
      ListBoxItem item = sender as ListBoxItem;
      if (item != null) {
        MpdChannel ch = item.Content as MpdChannel;
        if (ch != null){
          bool res = false;
          try{
            if (ch.Subscribed)
              res = m_Mpc.ChannelUnsubscribe(ch.Name);
            else
              res = m_Mpc.ChannelSubscribe(ch.Name);
          }catch (Exception ex){
            ShowException(ex);
            return;
          }
          if (res)
            ch.Subscribed = !ch.Subscribed;
        }
      }
    }    
    #endregion

    private void Label_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
      context.View = false;
    }

    private void Label_PreviewMouseLeftButtonDown_1(object sender, MouseButtonEventArgs e)
    {
      context.View = true;
    }

    private void lstAlbums_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
      var i = lstAlbums.InputHitTest(e.GetPosition(lstAlbums)) as FrameworkElement;
      if (i != null && i.DataContext != null && i.DataContext == lstAlbums.SelectedItem)
        context.View = true;
    }

    private void lstInfoItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                ((dynamic)(sender as FrameworkElement).DataContext).Uri.ToString()
            ) { Verb = "Open" });
        }
        catch { }
    }

    private void lstTracks_KeyDown(object sender, KeyEventArgs e)
    {
      if (e.Key == Key.Enter)
      {
        if (Keyboard.Modifiers == ModifierKeys.Shift || Keyboard.Modifiers == ModifierKeys.None)
          ContextMenu_Click(new MenuItem() { Name = "mnuAdd" }, null);
        else if (Keyboard.Modifiers == ModifierKeys.Control)
          ContextMenu_Click(new MenuItem() { Name = "mnuAddPlay" }, null);
        else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
          ContextMenu_Click(new MenuItem() { Name = "mnuAddReplacePlay" }, null);
        e.Handled = true;
      }
    }

    private void lstAlbums_KeyDown(object sender, KeyEventArgs e)
    {
      if (e.Key == Key.Enter)
      {
        if (Keyboard.Modifiers == ModifierKeys.Shift)
          ContextMenu_Click(new MenuItem() { Name = "mnuAdd" }, null);
        else if (Keyboard.Modifiers == ModifierKeys.Control)
          ContextMenu_Click(new MenuItem() { Name = "mnuAddPlay" }, null);
        else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
          ContextMenu_Click(new MenuItem() { Name = "mnuAddReplacePlay" }, null);
        else
          context.View ^= true;
        e.Handled = true;
      }
    }

    private void lstArtist_KeyDown(object sender, KeyEventArgs e)
    {
      if (e.Key == Key.Enter)
      {
        lstAlbums.Focus();
      }
    }

    private void TabBackwards(object sender, KeyEventArgs e)
    {
      if (e.Key == Key.Tab && Keyboard.Modifiers == ModifierKeys.None && !(sender == lstAlbums && context.View))
      {
        var element = sender as UIElement;
        if (element != null)
        {
          element.MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous));
          e.Handled = true;
        }
      }
    }
  }
}
