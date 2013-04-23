//    WpfMpdClient
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
using System.Threading;
using System.ComponentModel;
using Libmpc;

namespace WpfMpdClient
{
  public class ListboxEntry : INotifyPropertyChanged
  {
    public event PropertyChangedEventHandler PropertyChanged;
    EntryType m_Type;
    string m_Artist;
    string m_Album;
    bool m_Head = true;
    bool m_Selected;
    string m_Display;
    System.Collections.ObjectModel.ObservableCollection<ListboxEntry> m_Related;
    Uri m_ImageUrl = null;

    public enum EntryType
    {
      Artist,
      Album
    }

    public EntryType Type
    {
      get { return m_Type; }
      set
      {
        m_Type = value;
        OnPropertyChanged("Type");
      }
    }

    public string Artist
    {
      get { return m_Artist; }
      set
      {
        m_Artist = value;
        OnPropertyChanged("Artist");
      }
    }

    public string Album
    {
      get { return m_Album; }
      set
      {
        m_Album = value;
        OnPropertyChanged("Album");
      }
    }

    public bool Head
    {
      get { return m_Head; }
      set
      {
        m_Head = value;
        OnPropertyChanged("Visible");
      }
    }

    public bool Selected
    {
      get { return m_Selected; }
      set
      {
        m_Selected = value;
        OnPropertyChanged("Selected");
        OnPropertyChanged("Visible");
      }
    }

    public bool Visible
    {
      get { return m_Selected || m_Head; }
      set
      {
        Head = value;
      }
    }

    public string Display
    {
      get { return m_Display; }
      set
      {
        m_Display = value;
        OnPropertyChanged("Display");
      }
    }

    public System.Collections.ObjectModel.ObservableCollection<ListboxEntry> Related
    {
      get { return m_Related; }
      set
      {
        m_Related = value;
        OnPropertyChanged("Related");
      }
    }

    public Func<IEnumerable<MpdFile>> Tracks { get; set; }

    public static string ArtistKey(string Artist)
    {
      return string.Format("{0}_{1}_", EntryType.Artist.ToString(), Artist);
    }
    public string Key
    {
      get
      {
        return string.Format("{0}_{1}_{2}", Type.ToString(), Artist, Album);
      }
    }

    public Uri ImageUrl
    {
      get { return m_ImageUrl; }
      set
      {
        m_ImageUrl = value;
        OnPropertyChanged("ImageUrl");
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

  public static class ListboxUtils
  {
    public static ListboxEntry Selected(this System.Windows.Controls.ListBox listbox)
    {
      return listbox == null ? null : listbox.SelectedItem as ListboxEntry;
    }

    public static string Artist(this ListboxEntry entry)
    {
      return (entry == null || entry.Artist == Mpc.NoArtist ? null : entry.Artist) ?? "";
    }

    public static string Album(this ListboxEntry entry)
    {
      return (entry == null || entry.Artist == Mpc.NoAlbum || entry.Album == Mpc.NoAlbum ? null : entry.Album) ?? "";
    }
  }

  public class ArtDownloader
  {
    bool m_Working = false;
    int m_Downloaders = 0;
    int m_MaxDownloaders = 5;
    HashSet<ListboxEntry> m_Entries = new HashSet<ListboxEntry>();
    Mutex m_Mutex = new Mutex();
    Mutex m_IndexMutex = new Mutex();

    static readonly Dictionary<string, Uri> cache = new Dictionary<string, Uri>();
    Dictionary<string, Uri> m_Cache = cache;

    public ArtDownloader(Settings m_Settings, int m_MaxDownloaders = 10)
    {
      this.m_Settings = m_Settings;
      this.m_MaxDownloaders = m_MaxDownloaders;
    }

    public void Start()
    {
      if (!m_Working)
        System.Threading.ThreadPool.QueueUserWorkItem(new System.Threading.WaitCallback(Worker));
    }

    public void Stop()
    {
      m_Working = false;
    }

    public bool GetFromCache(ListboxEntry entry)
    {
      Uri uri = null;
      if (m_Cache.TryGetValue(entry.Key, out uri)) {
        entry.ImageUrl = uri;
        return uri != null || entry.Tracks == null;
      }
      else if (entry.Type == ListboxEntry.EntryType.Artist && entry.Artist != null)
      {
        var url = DiskImageCache.GetFromCache(new Uri(entry.Artist, UriKind.Relative), entry.Artist);
        if (!string.IsNullOrEmpty(url))
        {
          m_Cache[entry.Key] = new Uri(url);
          return true;
        }
      }

      return false;
    }

    public void Now(ListboxEntry entry)
    {
      Add(entry, 0);
    }

    public void Soon(ListboxEntry entry)
    {
      Add(entry, -1);
    }

    void Add(ListboxEntry entry, int index)
    {
      if (GetFromCache(entry))
        return;

      m_Mutex.WaitOne();
      m_Entries.Add(entry);
      m_Mutex.ReleaseMutex();
    }

    private void Worker(object state)
    {
      m_Working = true;
      while (m_Working) {
        if (m_Entries.Count > 0) {
          while (m_Downloaders < m_MaxDownloaders && m_Entries.Count > 0) {
            m_Mutex.WaitOne();
            ListboxEntry entry = m_Entries.FirstOrDefault();
            if (entry != null)
              m_Entries.Remove(entry);
            m_Mutex.ReleaseMutex();

            m_IndexMutex.WaitOne();
            m_Downloaders++;
            m_IndexMutex.ReleaseMutex();

            if (entry != null)
              System.Threading.ThreadPool.QueueUserWorkItem(new System.Threading.WaitCallback(Downloader), entry);
          }
          System.Threading.Thread.Sleep(50);
        } else
          System.Threading.Thread.Sleep(50);
      }
    }

    private Settings m_Settings;
    static readonly string[] filenames = new string[] { "cover", "folder", "album" };
    static readonly string[] exts = new string[] { "jpg", "png", "gif" };

    public static bool TryGet(Uri uri)
    {
      try
      {
        var req = System.Net.WebRequest.Create(uri);
        using (var resp = (System.Net.HttpWebResponse)req.GetResponse())
          return resp.StatusCode == System.Net.HttpStatusCode.OK;
      }
      catch { return false; }
    }

    public static IEnumerable<Uri> ImageUris(IEnumerable<string> paths)
    {
      return
        from path in paths
        from file in filenames
        from ext in exts
        select new Uri(new Uri("http://" + Settings.Instance.ServerAddress), System.IO.Path.Combine(path, file + "." + ext));
    }

    static readonly System.Text.RegularExpressions.Regex dir = new System.Text.RegularExpressions.Regex(@"^(.*)/(?:\\/|[^/]*)$");
    public static IEnumerable<Uri> ImageUris(IEnumerable<MpdFile> tracks)
    {
      return ImageUris((tracks ?? new MpdFile[0]).Select(t => dir.Replace(t.File, "$1")).Distinct());
    }

    public static Uri ImageUri(IEnumerable<MpdFile> tracks)
    {
      return ImageUris(tracks)
        .Where(TryGet)
        .FirstOrDefault();
    }

    public static Uri ImageUri(params string[] paths)
    {
      return ImageUris(paths)
        .Where(TryGet)
        .FirstOrDefault();
    }

    public static Uri ImageUri(params MpdFile[] tracks)
    {
      return ImageUri((IEnumerable<MpdFile>)tracks);
    }

    static readonly System.Text.RegularExpressions.Regex cid = new System.Text.RegularExpressions.Regex(@".*[(](?:[^)]*?\s)*([^)]+)[)]\s*$");
    private void Downloader(object state)
    {
      ListboxEntry entry = state as ListboxEntry;
      try
      {
        Uri uri = null;
        if (m_Cache.TryGetValue(entry.Key, out uri))
        {
          entry.ImageUrl = uri;
          if (uri != null || entry.Tracks == null)
            return;
        }
        string url = null;
        if (entry.Type == ListboxEntry.EntryType.Album)
        {
          var m = cid.Match(entry.Album());
          if (m.Success)
            uri = ImageUri(System.IO.Path.Combine("cid", m.Groups[1].Value));
        }
        if (url == null && uri == null && entry.Tracks != null)
          uri = ImageUri(entry.Tracks());
        if (url == null && uri == null)
        {
          if (entry.Type == ListboxEntry.EntryType.Artist)
            url = LastfmScrobbler.GetArtistArt(entry.Artist, Scrobbler.ImageSize.medium);
          else
            url = LastfmScrobbler.GetAlbumArt(entry.Artist, entry.Album, Scrobbler.ImageSize.medium);
        }
        if (!string.IsNullOrEmpty(url))
          uri = new Uri(url);
        if (uri != null)
        {
          entry.ImageUrl = uri;
          if (entry.Type != ListboxEntry.EntryType.Artist)
          {
            Uri a;
            m_Cache.TryGetValue(ListboxEntry.ArtistKey(entry.Artist), out a);
            if (a != null)
              m_Cache[entry.Artist] = a;
          }
        }
        m_Cache[entry.Key] = entry.ImageUrl;
      } catch (Exception) {
      } finally {
        m_IndexMutex.WaitOne();
        m_Downloaders--;
        m_IndexMutex.ReleaseMutex();
      }
    }
  }
}
