﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="ImageLoader.cs" company="Bryan A. Woodruff">
//   Copyright (c) 2011 Bryan A. Woodruff.
// </copyright>
// <summary>
//   The ImageLoader class is a derived class of System.Windows.Controls.Image.
//   It uses BitmapImage as a source to load the associated ImageUri and displays
//   an initial image in place until the image load has completed.
// </summary>
// <license>
//   Microsoft Public License (Ms-PL)
//   
//   This license governs use of the accompanying software. If you use the software, you accept this license. 
//   If you do not accept the license, do not use the software.
//   
//   * Definitions
//   The terms "reproduce," "reproduction," "derivative works," and "distribution" have the same meaning here 
//   as under U.S. copyright law. A "contribution" is the original software, or any additions or changes to the 
//   software. A "contributor" is any person that distributes its contribution under this license. 
//   "Licensed patents" are a contributor's patent claims that read directly on its contribution.
//
//   * Grant of Rights
//   (A) Copyright Grant- Subject to the terms of this license, including the license conditions and 
//       limitations in section 3, each contributor grants you a non-exclusive, worldwide, royalty-free copyright 
//       license to  reproduce its contribution, prepare derivative works of its contribution, and distribute its 
//       contribution  or any derivative works that you create.
//   (B) Patent Grant- Subject to the terms of this license, including the license conditions and limitations in 
//       section 3, each contributor grants you a non-exclusive, worldwide, royalty-free license under its 
//       licensed  patents to make, have made, use, sell, offer for sale, import, and/or otherwise dispose of its 
//       contribution in the software or derivative works of the contribution in the software.
//   
//   * Conditions and Limitations
//   (A) No Trademark License- This license does not grant you rights to use any contributors' name, logo, or 
//       trademarks.
//   (B) If you bring a patent claim against any contributor over patents that you claim are infringed by the 
//       software, your patent license from such contributor to the software ends automatically.
//   (C) If you distribute any portion of the software, you must retain all copyright, patent, trademark, and 
//       attribution notices that are present in the software.
//   (D) If you distribute any portion of the software in source code form, you may do so only under this license 
//       by including a complete copy of this license with your distribution. If you distribute any portion of the 
//       software in compiled or object code form, you may only do so under a license that complies with this 
//       license.
//   (E) The software is licensed "as-is." You bear the risk of using it. The contributors give no express warranties, 
//       guarantees, or conditions. You may have additional consumer rights under your local laws which this license 
//       cannot change. To the extent permitted under your local laws, the contributors exclude the implied warranties 
//       of merchantability, fitness for a particular purpose and non-infringement.
// </license>
// --------------------------------------------------------------------------------------------------------------------

namespace WpfMpdClient
{
  using System;
  using System.Windows;
  using System.Windows.Controls;
  using System.Windows.Media;
  using System.Windows.Media.Imaging;
  using System.Collections.Generic;
  using System.Threading;
  using System.IO;
  using System.Linq;
  using System.Collections.Concurrent;

  public class DiskImageCache
  {
    static string m_TempPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), System.Reflection.Assembly.GetExecutingAssembly().GetName().Name.ToString(), "Art");
    static ConcurrentDictionary<Uri, string> m_Cache = new ConcurrentDictionary<Uri, string>();
    static DiskImageCache()
    {
      Directory.CreateDirectory(m_TempPath);
    }

    [ThreadStatic]
    static System.Security.Cryptography.MD5 hash;

    static string Hash(string data)
    {
      var s = new System.Text.StringBuilder();
      if (hash == null)
        hash = System.Security.Cryptography.MD5.Create();
      foreach (var b in hash.ComputeHash(System.Text.Encoding.Unicode.GetBytes(data)))
        s.AppendFormat("{0:x2}", b);
      return s.ToString();
    }

    static string TempPath(object o)
    {
      return Path.Combine(m_TempPath, Hash((o ?? Guid.NewGuid()).ToString()) + ".jpg");
    }

    public static string Lookup(params object[] keys)
    {
      return keys.Select(TempPath).FirstOrDefault(File.Exists);
    }

    public static string GetFromCache(Uri uri, string key)
    {
      string result = string.Empty;
      if (!m_Cache.TryGetValue(uri, out result))
      {
        result = Lookup(uri, key);
        if (result != null)
          m_Cache[uri] = result;
      }
      return result;
    }

    public static void AddToCache(BitmapImage image, string key = null)
    {
      try {
        JpegBitmapEncoder encoder = new JpegBitmapEncoder();
        string path = TempPath(key ?? (object)image.UriSource);
        encoder.Frames.Add(BitmapFrame.Create(image));
        using (var filestream = new FileStream(path, FileMode.Create))
          encoder.Save(filestream);

        m_Cache[image.UriSource] = path;
      } catch (Exception) { 
      }
    }

    public static void DeleteCacheFiles()
    {
      hash.Dispose();
      // TODO: Delete old cache files
    }
  }

  /// <summary>
  /// Defines the ImageLoader class, derived from System.Windows.Controls.Image
  /// </summary>
  public class ImageLoader : Image
  {
    /// <summary>
    /// The ImageUri dependency property.
    /// </summary>
    public static readonly DependencyProperty ImageUriProperty = DependencyProperty.Register(
        "ImageUri", typeof(Uri), typeof(ImageLoader), new PropertyMetadata(null, OnImageUriChanged));

    public static readonly DependencyProperty ImageKeyProperty = DependencyProperty.Register(
        "ImageKey", typeof(string), typeof(ImageLoader));

    private static void OnImageUriChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
      ImageLoader This = d as ImageLoader;
      if (This != null)
        This.DownloadImage();
    }

    /// <summary>
    /// Storage for the loaded image.
    /// </summary>
    private BitmapImage loadedImage;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageLoader"/> class.
    /// </summary>
    public
    ImageLoader()
    {
      Loaded += OnLoaded;
    }

    /// <summary>
    /// Gets or sets the load failed image path string.
    /// </summary>
    public
    string
    LoadFailedImage
    {
      get;
      set;
    }

    /// <summary>
    /// Gets or sets the ImageUri property.
    /// </summary>
    public Uri ImageUri
    {
      get
      {
        return GetValue(ImageUriProperty) as Uri;
      }

      set
      {
        SetValue(ImageUriProperty, value);
      }
    }

    /// <summary>
    /// Gets or sets the ImageKey property.
    /// </summary>
    public string ImageKey
    {
      get
      {
        return GetValue(ImageKeyProperty) as string;
      }

      set
      {
        SetValue(ImageKeyProperty, value);
      }
    }

    /// <summary>
    /// Gets or sets the initial image path string.
    /// </summary>
    public
    string
    InitialImage
    {
      get;
      set;
    }

    /// <summary>
    /// Gets or sets the source property which forwards to the base Image class.
    /// This is made an internal property in ImageLoader to prevent confusion with
    /// the base class.  
    /// This property is managed as a result of the bitmap load operations.
    /// </summary>
    private
    new
    ImageSource
    Source
    {
      get
      {
        return base.Source;
      }

      set
      {
        base.Source = value;
      }
    }

    private void DownloadImage()
    {
      if (ImageUri == null)
        return;

      string fromCache = DiskImageCache.GetFromCache(ImageUri, ImageKey);
      loadedImage = new BitmapImage();
      loadedImage.BeginInit();
      loadedImage.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
      loadedImage.CacheOption = BitmapCacheOption.OnDemand;
      loadedImage.DownloadCompleted += OnDownloadCompleted;
      loadedImage.DownloadFailed += OnDownloadFailed;
      loadedImage.DecodeFailed += OnDecodeFailed;
      loadedImage.UriSource = !string.IsNullOrEmpty(fromCache) ? new Uri(fromCache) : ImageUri;
      loadedImage.EndInit();
      if (!string.IsNullOrEmpty(fromCache))
        Source = loadedImage;
    }

    /// <summary>
    /// Handles the Loaded event for the ImageLoader class.
    /// </summary>
    /// <param name="sender">
    /// The sender object.
    /// </param>
    /// <param name="e">
    /// The event arguments.
    /// </param>
    private
    void
    OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
      // Loading the specified image      
      DownloadImage();

      // The image may be cached, in which case we will not use the initial image
      if (loadedImage != null && !loadedImage.IsDownloading) {
        Source = loadedImage;
      } else {
        // Create InitialImage source if path is specified
        if (!string.IsNullOrEmpty(InitialImage)) {
          BitmapImage initialImage = new BitmapImage();

          // Load the initial bitmap from the local resource
          initialImage.BeginInit();
          initialImage.UriSource = new Uri(InitialImage, UriKind.Relative);
          initialImage.EndInit();

          // Set the initial image as the image source
          Source = initialImage;
        }
      }

      e.Handled = true;
    }

    /// <summary>
    /// Handles the download failure event.
    /// </summary>
    /// <param name="sender">
    /// The sender object.
    /// </param>
    /// <param name="e">
    /// The event arguments.
    /// </param>
    private
    void
    OnDownloadFailed(
        object sender,
        ExceptionEventArgs e)
    {
      if (!string.IsNullOrWhiteSpace(LoadFailedImage)) {
        BitmapImage failedImage = new BitmapImage();

        // Load the initial bitmap from the local resource
        failedImage.BeginInit();
        failedImage.UriSource = new Uri(LoadFailedImage, UriKind.Relative);
        failedImage.EndInit();
        Source = failedImage;
      }
    }

    /// <summary>
    /// Handles the DownloadCompleted event.
    /// </summary>
    /// <param name="sender">
    /// The sender object.
    /// </param>
    /// <param name="e">
    /// The event arguments.
    /// </param>
    private
    void
    OnDownloadCompleted(
        object sender,
        EventArgs e)
    {
      Source = loadedImage;
      DiskImageCache.AddToCache(loadedImage, ImageKey);
    }

    private void OnDecodeFailed<TEventArgs>(object sender, TEventArgs e)
    {
      if (!string.IsNullOrWhiteSpace(LoadFailedImage)) {
        BitmapImage failedImage = new BitmapImage();

        // Load the initial bitmap from the local resource
        failedImage.BeginInit();
        failedImage.UriSource = new Uri(LoadFailedImage, UriKind.Relative);
        failedImage.EndInit();
        Source = failedImage;
      }
    }
  }
}
