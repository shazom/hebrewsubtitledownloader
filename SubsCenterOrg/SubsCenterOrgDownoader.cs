﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using SubtitleDownloader.Core;
using SubtitleDownloader.Util;

namespace SubsCenterOrg
{
  public class SubsCenterOrgDownoader : ISubtitleDownloader
  {
    private const string BaseUrl = "http://www.subscenter.org";
    private const string ExactMovieUrl = "http://www.subscenter.org/he/subtitle/movie/";
    private const string ExactSeriesUrl = "http://www.subscenter.org/he/subtitle/series/";
    private const string SearchUrlBase = "http://www.subscenter.org/he/subtitle/search/?";
    private const string DownloadPageUrl = "http://www.subscenter.org/he/subtitle/download/";
    private const string HeSubtitleMovie = "/he/subtitle/movie/";
    private const string HeSubtitleSeries = "/he/subtitle/series/";
    private const string Error404 = "Error 404";


    public int SearchTimeout { get; set; }

    public string GetName()
    {
      return "SubCenter.Org";
    }

    public List<Subtitle> SearchSubtitles(SearchQuery query)
    {
      var gotMatch = false;
      var retries = 0;

      // try guessing exact movie url
      var exactMovieUrl = ExactMovieUrl + query.Query.Replace(" ", "-").ToLower() + "/";

      //  download "exact" page
      var web = new HtmlWeb();
      var moviePage = web.Load(exactMovieUrl);

      // handle "Error 404" - page not found
      if (IsPageNotFound(moviePage) || !YearMatch(moviePage, query.Year) || !TitleMatch(moviePage, query.Query))
      {
        // search url
        var queryUrl = SearchUrlBase + "q=" + query.Query;
        var queryPage = web.Load(queryUrl);
        var htmlNodeCollection = queryPage.DocumentNode.SelectNodes("//a");
        foreach (var node in htmlNodeCollection)
        {
          var attributeValue = node.GetAttributeValue("href", string.Empty);
          if (attributeValue.StartsWith(HeSubtitleMovie))
          {
            // download new page
            moviePage = web.Load(BaseUrl + attributeValue);
            if (YearMatch(moviePage, query.Year) && TitleMatch(moviePage, query.Query))
            {
              gotMatch = true;
              break;
            }
            if (++retries >= 3)
            {
              break;
            }
          }
        }
      }
      else
      {
        gotMatch = true;
      }

      // verify year and title
      if (gotMatch)
      {
        return Search(moviePage, query);
      }
      return new List<Subtitle>();

    }

    public List<Subtitle> SearchSubtitles(EpisodeSearchQuery query)
    {
      var gotMatch = false;
      var retries = 0;

      // try guessing exact episode url
      var exactEpisodeUrl = ExactSeriesUrl + query.SerieTitle.Replace(" ", "-").ToLower() + "/" + query.Season + "/" + query.Episode;

      //  download "exact" page
      var web = new HtmlWeb();
      var moviePage = web.Load(exactEpisodeUrl);

      // handle "Error 404" - page not found
      if (IsPageNotFound(moviePage) || !SeasonAndEpisodeMatch(moviePage, query.Season, query.Episode))
      {
        // search url
        var queryUrl = SearchUrlBase + "q=" + query.SerieTitle;
        var queryPage = web.Load(queryUrl);
        var htmlNodeCollection = queryPage.DocumentNode.SelectNodes("//a");
        foreach (var node in htmlNodeCollection)
        {
          var attributeValue = node.GetAttributeValue("href", string.Empty);
          if (attributeValue.StartsWith(HeSubtitleSeries))
          {
            // download new page
            moviePage = web.Load(BaseUrl + attributeValue + "/" + query.Season + "/" + query.Episode);
            if (SeasonAndEpisodeMatch(moviePage, query.Season, query.Episode))
            {
              gotMatch = true;
              break;
            }
            if (++retries >= 3)
            {
              break;
            }
          }
        }
      }
      else
      {
        gotMatch = true;
      }

      // verify
      if (gotMatch)
      {
        return Search(moviePage, query);
      }
      return new List<Subtitle>();
    }

    public List<Subtitle> SearchSubtitles(ImdbSearchQuery query)
    {
      throw new NotSupportedException();
    }

    public List<FileInfo> SaveSubtitle(Subtitle subtitle)
    {
      var url = DownloadPageUrl + GetLanguagePath(subtitle.LanguageCode) + "/" + subtitle.Id + "/?v=" + subtitle.FileName;
      var archiveFile = Path.GetTempFileName() + ".zip";

      var client = new WebClient();
      client.DownloadFile(url, archiveFile);

      try
      {
        List<FileInfo> extractFilesFromZipOrRarFile = FileUtils.ExtractFilesFromZipOrRarFile(archiveFile);
        return extractFilesFromZipOrRarFile;
      }
      catch (Exception)
      {
        return new List<FileInfo>();
      }
    }


    /**
     */
    private static List<Subtitle> Search(HtmlDocument page, SubtitleSearchQuery query)
    {
      var subtitles = new List<Subtitle>();

      var subtitlesGroups = GetSubtitlesGroups(page);

      var parsedSubtitles = ParseSubtitlesGroups(subtitlesGroups);

      foreach (var language in parsedSubtitles)
      {
        foreach (var group in language.Value)
        {
          foreach (var quality in group.Value)
          {
            foreach (var index in quality.Value)
            {
              // parse each subtitle here
              // mandatory fields: id, subtitle_version, 
              var id = "";
              var subtitleVersion = "";
              foreach (var param in index.Value)
              {
                switch (param.Key)
                {
                  case "\"id\"":
                    {
                      id = param.Value;
                      break;
                    }
                  case "\"subtitle_version\"":
                    {
                      subtitleVersion = param.Value;
                      break;
                    }
                  default:
                    {
                      break;
                    }
                }
              }

              // Add subtitles - todo - add only if language is in query
              if (id != "" && subtitleVersion != "")
              {
                var version = subtitleVersion.Replace("\"", "");
                string languageName;
                LanguageShortToLongCodeDictionary.TryGetValue(language.Key.Replace("\"", ""), out languageName);
                var languageCode = Languages.GetLanguageCode(languageName);

                if (query.HasLanguageCode(languageCode))
                {
                  subtitles.Add(new Subtitle(id, version, version, languageCode));
                }
              }
            }
          }
        }
      }

      return subtitles;
    }

    /**
     * Return the subtitles_groups from java script
     */
    private static string GetSubtitlesGroups(HtmlDocument page)
    {
      var htmlNodeCollection = page.DocumentNode.SelectNodes("//script");
      foreach (var node in htmlNodeCollection)
      {
        if (node.InnerText.Contains("subtitles_groups"))
        {
          Match match = Regex.Match(node.InnerText, "subtitles_groups = (?<subs>.*)");
          if (match.Groups.Count == 2)
          {
            return match.Groups[1].Value;
          }
        }
      }
      throw new Exception("cannot find subtitles.");
    }

    /**
     * Returns subtitles as a map of:
     * Dictionary<language, Dictionary<group, Dictionary<quality, Dictionary<index, Dictionary<name, value>>>>>
     */
    private static Dictionary<string, Dictionary<string, Dictionary<string, Dictionary<string, Dictionary<string, string>>>>> ParseSubtitlesGroups(string subtitlesGroup)
    {
      // tokens
      const string splitPattern = @"({)|(})|(, )|(: )";
      var tokens = Regex.Split(subtitlesGroup, splitPattern);

      // parsing state
      var level = 0;
      var isValue = false;
      var output = new Dictionary<string, Dictionary<string, Dictionary<string, Dictionary<string, Dictionary<string, string>>>>>();
      Dictionary<string, Dictionary<string, Dictionary<string, Dictionary<string, string>>>> currentLanguage = null;
      Dictionary<string, Dictionary<string, Dictionary<string, string>>> currentGroup = null;
      Dictionary<string, Dictionary<string, string>> currentQuality = null;
      Dictionary<string, string> currentIdx = null;
      var currentName = "";

      foreach (var token in tokens)
      {
        if (token.Length == 0)
        {
          continue;
        }

        switch (token[0])
        {
          case '{':
            {
              level++;
              break;
            }
          case '}':
            {
              level--;
              break;
            }
          case ',':
            {
              isValue = false;
              break;
            }
          case ':':
            {
              if (level == 5)
              {
                isValue = true;
              }
              break;
            }
          default:
            {
              switch (level)
              {
                case 1:
                  {
                    // language
                    currentLanguage = new Dictionary<string, Dictionary<string, Dictionary<string, Dictionary<string, string>>>>();
                    output.Add(token, currentLanguage);
                    break;
                  }
                case 2:
                  {
                    // group
                    currentGroup = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();
                    if (currentLanguage != null)
                    {
                      currentLanguage.Add(token, currentGroup);
                    }
                    break;
                  }
                case 3:
                  {
                    // quality
                    currentQuality = new Dictionary<string, Dictionary<string, string>>();
                    if (currentGroup != null)
                    {
                      currentGroup.Add(token, currentQuality);
                    }
                    break;
                  }
                case 4:
                  {
                    // index 
                    currentIdx = new Dictionary<string, string>();
                    if (currentQuality != null)
                    {
                      currentQuality.Add(token, currentIdx);
                    }
                    break;
                  }
                default:
                  {
                    // name/value
                    if (!isValue)
                    {
                      currentName = token;
                    }
                    else
                    {
                      if (currentIdx != null && !currentIdx.ContainsKey(currentName))
                      {
                        currentIdx.Add(currentName, token);
                      }
                      isValue = false;
                    }
                  } // end inner default
                  break;
              } // end inner switch
            } // end default
            break;
        } // end switch
      }

      // output
      return output;
    }

    /**
     * Return language path name ("en" for English, "he" for Hebrew", etc.)
     */
    private static string GetLanguagePath(string language)
    {
      if (language != null && language.Length >= 2)
      {
        return language.Substring(0, 2);
      }
      throw new Exception("Unknow language " + language);
    }

    /**
 * Return true if "Error 404" is found in page
 */
    private static bool IsPageNotFound(HtmlDocument moviePage)
    {
      // Get all img nodes and search for "Error 404"
      var images = moviePage.DocumentNode.SelectNodes("//img");
      foreach (var node in images)
      {
        var alt = node.GetAttributeValue("alt", string.Empty);
        if (alt.Equals(Error404))
        {
          return true;
        }
      }
      return false;
    }

    /**
     * Return true if year matches
     */
    private static bool YearMatch(HtmlDocument moviePage, int? expectedYear)
    {
      if (expectedYear == null)
      {
        return true;
      }

      try
      {
        var year = int.Parse(Regex.Match(moviePage.DocumentNode.SelectNodes("//h1")[0].ParentNode.InnerText, "\\d+").Value);
        return Math.Abs((int) (expectedYear - year)) <= 1;
      }
      catch
      {
        return false;
      }
    }

    /**
     * Return true if title matches
     */
    private static bool TitleMatch(HtmlDocument moviePage, string expectedTitle)
    {
      try
      {
        var title = moviePage.DocumentNode.SelectNodes("//h3")[0].InnerText.Trim();
        return expectedTitle.Equals(title, StringComparison.OrdinalIgnoreCase);
      }
      catch
      {
        return false;
      }
    }

    /**
     * Return true if Season and Episode match
     * 
     */
    private static bool SeasonAndEpisodeMatch(HtmlDocument moviePage, int expectedSeason, int expectedEpisode)
    {
      try
      {
        var spanPageName = moviePage.DocumentNode.SelectNodes("//span");
        foreach (var node in spanPageName)
        {
          var atr = node.GetAttributeValue("class", string.Empty);
          if (atr.Equals("pageName"))
          {
            var seasonEpisodeText = node.InnerText.Trim();
            var matches = Regex.Matches(seasonEpisodeText, "\\d+");
            var season = int.Parse(matches[0].Value);
            var episode = int.Parse(matches[1].Value);

            return ((season == expectedSeason) && (episode == expectedEpisode));
          }
        }

        return false;
      }
      catch
      {
        return false;
      }
    }


    private static readonly Dictionary<string, string> LanguageShortToLongCodeDictionary = new Dictionary<string, string>
                                                                              {
                                                                                {"ar", "Arabic"},
                                                                                {"be", "Belarus"},
                                                                                {"bg", "Bulgarian"},
                                                                                {"br", "Brazilian"},
                                                                                {"bs", "Bosnian"},
                                                                                {"ca", "Catalan"},
                                                                                {"cs", "Czech"},
                                                                                {"da", "Danish"},
                                                                                {"de", "German"},
                                                                                {"en", "English"},
                                                                                {"es", "Spanish"},
                                                                                {"et", "Estonian"},
                                                                                {"fi", "Finnish"},
                                                                                {"fr", "French"},
                                                                                {"ga", "Irish"},
                                                                                {"gr", "Greek"},
                                                                                {"he", "Hebrew"},
                                                                                {"hi", "Hindi"},
                                                                                {"hu", "Hungarian"},
                                                                                {"hr", "Croatian"},
                                                                                {"is", "Icelandic"},
                                                                                {"id", "Indonesian"},
                                                                                {"it", "Italian"},
                                                                                {"ja", "Japanese"},
                                                                                {"ko", "Korean"},
                                                                                {"lv", "Latvian"},
                                                                                {"lt", "Lithuanian"},
                                                                                {"mk", "Macedonian"},
                                                                                {"ms", "Malay"},
                                                                                {"nl", "Dutch"},
                                                                                {"no", "Norwegian"},
                                                                                {"pl", "Polish"},
                                                                                {"pt", "Portuguese"},
                                                                                {"ro", "Romanian"},
                                                                                {"ru", "Russian"},
                                                                                {"sr", "Serbian"},
                                                                                {"sk", "Slovak"},
                                                                                {"sl", "Slovenian"},
                                                                                {"sv", "Swedish"},
                                                                                {"th", "Thai"},
                                                                                {"tr", "Turkish"},
                                                                                {"uk", "Ukrainian"},
                                                                                {"vi", "Vietnamese"},
                                                                                {"zh", "Chinese"}
                                                                              };

  }
}
