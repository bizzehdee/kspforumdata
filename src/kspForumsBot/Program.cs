using Fizzler.Systems.HtmlAgilityPack;
using HtmlAgilityPack;
using kerbalScrape;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace kspForumsBot
{
    internal class Program
    {
        static List<ForumStructure> Forums = new List<ForumStructure>();
        static int failures = 0;
        static string basePath = "."; //change this to the dest directory you want

        static async Task Main(string[] args)
        {
            await DownloadForumUploads();

            if (!File.Exists($"{basePath}/ksp-forum-def.json"))
            {
                return;
            }

            var json = await File.ReadAllTextAsync($"{basePath}/ksp-forum-def.json");
            var forums = JsonSerializer.Deserialize<ForumStructure[]>(json);

            foreach (var forum in forums)
            {
                await DownloadForum(forum, 3); //ive set this to 3 because there are no more than 3 pages per day of updates within a forum
            }
            await DownloadForumUploads();
        }

        private static async Task DownloadProfiles()
        {
            int maxProfileId = 234294;

            await Parallel.ForAsync(1, maxProfileId + 1, new ParallelOptions { MaxDegreeOfParallelism = 1 }, async (profileId, ct) =>
            {
                await DownloadProfile(profileId);
            });
        }

        private static async Task DownloadProfile(int profileId)
        {
            var profilesPath = $"{basePath}/profiles";

            if (!Directory.Exists(profilesPath))
            {
                Directory.CreateDirectory(profilesPath);
            }

            if(File.Exists($"{profilesPath}/{profileId}.json"))
            {
                return;
            }

            var html = string.Empty;

            try
            {
                html = await FetchURL($"https://forum.kerbalspaceprogram.com/profile/{profileId}-x/");

            }
            catch (Exception ex)
            {

            }

            var parser = new HtmlDocument
            {
                OptionExtractErrorSourceText = true
            };

            parser.LoadHtml(html);

            var nameNode = parser.DocumentNode.QuerySelector("header h1");

            if(nameNode == null)
            {
                return;
            }

            var groupNode = parser.DocumentNode.QuerySelector("header .cProfileHeader_name span span span");
            var pfpNode = parser.DocumentNode.QuerySelector(".ipsUserPhoto_xlarge img");
            var joinedNode = parser.DocumentNode.QuerySelector("#elProfileStats time");
            var repNumNode = parser.DocumentNode.QuerySelector(".cProfileRepScore_points");
            var repDescNode = parser.DocumentNode.QuerySelector(".cProfileRepScore_title");

            var name = nameNode.InnerText.Trim();
            var group = groupNode?.InnerText?.Trim() ?? string.Empty;
            var pfpUrl = pfpNode.Attributes["src"].Value ?? string.Empty;
            var joinedDateTime = joinedNode.Attributes["datetime"].Value ?? string.Empty;
            var repNumText = repNumNode?.InnerText?.Trim() ?? "0";
            int.TryParse(repNumText, out var repNum);
            var repDesc = repDescNode?.InnerText?.Trim() ?? string.Empty;

            if(!pfpUrl.StartsWith("http"))
            {
                pfpUrl = $"https:{pfpUrl}";
            }

            var pfpUri = new Uri(pfpUrl);
            var absPath = pfpUri.AbsolutePath;

            var pfpFile = absPath.Trim('/');
            if(!pfpFile.StartsWith("uploads"))
            {
                pfpFile = $"{basePath}/uploads/{pfpFile}";
            }

            var pfpPath = pfpFile.Substring(0, pfpFile.LastIndexOf('/'));

            if (!Directory.Exists(pfpPath))
            {
                Directory.CreateDirectory(pfpPath);
            }

            if (!File.Exists(pfpFile))
            {
                await DownloadFileTo(pfpUrl, pfpFile);
            }

            var profile = new UserProfile
            {
                Id = profileId,
                Name = name,
                Group = group,
                JoinedDateTime = joinedDateTime,
                Reputation = repNum,
                ReputationDesc = repDesc,
                ProfileImage = pfpFile
            };

            var json = JsonSerializer.Serialize<UserProfile>(profile);
            await File.WriteAllTextAsync($"{profilesPath}/{profileId}.json", json);
        }

        static async Task DownloadForum(ForumStructure forum, int maxPages = -1)
        {
            await DownloadForumById(forum.Id, maxPages: maxPages);

            foreach (var item in forum.Forums)
            {
                await DownloadForum(item);
            }
        }

        private static async Task DownloadForumUploads()
        {
            var directories = Directory.EnumerateDirectories($"{basePath}/topics");

            var files = directories.SelectMany(d=> Directory.EnumerateFiles(d, "*-articles.json"));
            await Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (file, ct) =>
            {
                var json = await File.ReadAllTextAsync(file);
                var articleData = JsonSerializer.Deserialize<TopicArticlePage>(json);

                var parser = new HtmlDocument
                {
                    OptionExtractErrorSourceText = true
                };

                foreach (var article in articleData.Articles)
                {
                    parser.LoadHtml(article.Content);

                    var images = parser.DocumentNode.QuerySelectorAll("img");

                    foreach (var image in images)
                    {
                        var src = image.Attributes["src"].Value;
                        if (src.Contains("kerbal-forum-uploads.s3.us-west-2.amazonaws.com"))
                        {
                            if (!src.StartsWith("http"))
                            {
                                src = "https:" + src;
                            }

                            var subFileName = src.Substring(src.IndexOf("kerbal-forum-uploads.s3.us-west-2.amazonaws.com") + 48);
                            var fileToSave = $"{basePath}/uploads/{subFileName}";

                            var directoryRequired = fileToSave.Substring(0, fileToSave.LastIndexOf("/"));

                            if (!Directory.Exists(directoryRequired))
                            {
                                Directory.CreateDirectory(directoryRequired);
                            }

                            if (!File.Exists(fileToSave))
                            {
                                await DownloadFileTo(src, fileToSave);
                            }
                        }
                    }
                }
            });
        }

        static object lockObj = new object();

        private static async Task DownloadFileTo(string url, string filename)
        {
            var bytes = await FetchFileData(url);
            if (bytes == null || bytes.Length == 0)
            {
                return;
            }

            lock (lockObj)
            {
                File.WriteAllBytes(filename, bytes);
            }
        }

        private static async Task ConvertToMarkdown()
        {
            var converter = new ReverseMarkdown.Converter();

            var directories = Directory.EnumerateDirectories($"{basePath}/topics");

            var files = directories.SelectMany(d => Directory.EnumerateFiles(d, "*-articles.json"));
            foreach (var file in files)
            {
                var json = await File.ReadAllBytesAsync(file);
                var articleData = JsonSerializer.Deserialize<TopicArticlePage>(json);

                var newArticlePage = new TopicArticlePage
                {
                    ForumId = articleData.ForumId,
                    TopicId = articleData.TopicId,
                    CreatedById = articleData.CreatedById,
                    CreatedByName = articleData.CreatedByName,
                    CreatedDateTime = articleData.CreatedDateTime,
                    PageNum = articleData.PageNum,
                    TopicTitle = WebUtility.HtmlDecode(articleData.TopicTitle)
                };

                foreach (var article in articleData.Articles)
                {
                    var articleText = WebUtility.HtmlDecode(article.Content);
                    var articleMD = converter.Convert(articleText);

                    var newArticle = new TopicArticle
                    {
                        CreatedById = article.CreatedById,
                        CreatedByName = article.CreatedByName,
                        CreatedDateTime = article.CreatedDateTime,
                        Content = articleMD
                    };

                    newArticlePage.Articles.Add(newArticle);
                }

                
                var jsonstr = JsonSerializer.Serialize<TopicArticlePage>(newArticlePage);
                await File.WriteAllTextAsync($"{basePath}/topics/{newArticlePage.ForumId}/{newArticlePage.TopicId}-{newArticlePage.PageNum}-articles.json", jsonstr);
            }
        }

        static async Task DownloadForumById(int id, int startPage = 1, int maxPages = -1)
        {
            var topLevelHtml = await FetchURL($"https://forum.kerbalspaceprogram.com/forum/{id}-x/");

            var parser = new HtmlDocument
            {
                OptionExtractErrorSourceText = true
            };
            parser.LoadHtml(topLevelHtml);

            var lastPageNode = parser.DocumentNode.QuerySelector("li.ipsPagination_last a");

            var lastPage = 1;

            if (lastPageNode != null)
            {
                var lastPageStr = lastPageNode.Attributes["data-page"].Value;
                lastPage = Convert.ToInt32(lastPageStr);
            }

            if(maxPages > 0)
            {
                lastPage = Math.Min(lastPage, maxPages);
            }

            for (var p = startPage; p <= lastPage; p++)
            {
                await DownloadForumPage(id, p);
            }
        }

        static async Task DownloadForumPage(int forumId, int pageNum)
        {
            Console.WriteLine($"Scraping Forum ID: {forumId} Page {pageNum}");

            var topLevelHtml = await FetchURL($"https://forum.kerbalspaceprogram.com/forum/{forumId}-x/page/{pageNum}/");

            var parser = new HtmlDocument
            {
                OptionExtractErrorSourceText = true
            };
            parser.LoadHtml(topLevelHtml);

            var postsNodes = parser.DocumentNode.QuerySelectorAll(".cTopicList li");

            await Parallel.ForEachAsync(postsNodes, new ParallelOptions { MaxDegreeOfParallelism = 2 }, async (postNode, ct) =>
            {
                var ahrefNodes = postNode.QuerySelectorAll("h4.ipsDataItem_title a");
                var ahrefNode = ahrefNodes.FirstOrDefault(f => !f.HasClass("ipsTag_prefix"));
                var posterNode = postNode.QuerySelector(".ipsDataItem_meta a");
                var postedTimeNode = postNode.QuerySelector(".ipsDataItem_meta time");
                var lastPageNode = postNode.QuerySelector("span.ipsPagination_last a");

                if (posterNode == null || ahrefNode == null)
                {
                    return;
                }

                if (postNode.Attributes.Contains("data-rowid") == false)
                {
                    return;
                }

                var topicIdStr = postNode.Attributes["data-rowid"].Value.Trim();
                var topicId = Convert.ToInt32(topicIdStr);
                var topicUrl = ahrefNode.Attributes["href"].Value;
                var topicTitle = WebUtility.HtmlDecode(ahrefNode.InnerText.Trim());
                var postedBy = "Guest";
                var postedOn = postedTimeNode.Attributes["datetime"].Value;

                var posterUrl = posterNode.Attributes["href"].Value;

                var postedByIdStr = GetUserIdFromProfileUrl(posterUrl);
                var postedById = -1;

                if (!string.IsNullOrWhiteSpace(postedByIdStr))
                {
                    postedBy = posterNode.InnerText.Trim();
                    postedById = Convert.ToInt32(postedByIdStr);
                }

                var topicPageCount = 1;

                if (lastPageNode != null)
                {
                    var topicPageCountStr = lastPageNode.InnerText.Trim();
                    topicPageCount = Convert.ToInt32(topicPageCountStr);
                }

                var topic = new TopicMetadata
                {
                    TopicId = topicId,
                    ForumId = forumId,
                    CreatedByName = postedBy,
                    PageCount = topicPageCount,
                    TopicTitle = topicTitle,
                    TopicUrl = topicUrl,
                    CreatedDateTime = postedOn,
                    CreatedById = postedById
                };

                if (!Directory.Exists($"{basePath}/topics/{forumId}"))
                {
                    Directory.CreateDirectory($"{basePath}/topics/{forumId}");
                }

                var json = JsonSerializer.Serialize<TopicMetadata>(topic);
                await File.WriteAllTextAsync($"{basePath}/topics/{forumId}/{topicId}-meta.json", json, ct);

                await Parallel.ForAsync(1, topicPageCount + 1, new ParallelOptions { MaxDegreeOfParallelism = 1 }, async (tp, ct) =>
                {
                    await DownloadForumTopicData(topic, tp);
                });
            });
        }

        static async Task DownloadForumTopicData(TopicMetadata metadata, int pageNum)
        {
            if (File.Exists($"{basePath}/topics/{metadata.ForumId}/{metadata.TopicId}-{pageNum}-articles.json") && !(pageNum == metadata.PageCount || pageNum == metadata.PageCount - 1))
            {
                return;
            }

            Console.WriteLine($"Scraping: \"{metadata.TopicTitle}\" - Page {pageNum}");

            var url = metadata.TopicUrl.Trim('/');

            var topLevelHtml = await FetchURL($"{url}/page/{pageNum}/");

            var parser = new HtmlDocument
            {
                OptionExtractErrorSourceText = true
            };
            parser.LoadHtml(topLevelHtml);

            var articleNodes = parser.DocumentNode.QuerySelectorAll("article");

            var topicPage = new TopicArticlePage
            {
                TopicId = metadata.TopicId,
                TopicTitle = metadata.TopicTitle,
                ForumId = metadata.ForumId,
                CreatedDateTime = metadata.CreatedDateTime,
                CreatedByName = metadata.CreatedByName,
                CreatedById = metadata.CreatedById,
                PageNum = pageNum
            };

            foreach (var articleNode in articleNodes)
            {
                var contentNode = articleNode.QuerySelector("div[data-role='commentContent']");
                var authorNameNode = articleNode.QuerySelector("h3 a");
                var contentTimeNode = articleNode.QuerySelector("time");

                if (contentTimeNode == null)
                {
                    continue;
                }

                var contentDateTime = contentTimeNode.Attributes["datetime"].Value;
                var contentAuthorId = -1;
                var createdByName = "Guest";

                if (authorNameNode != null)
                {
                    string contentAuthorIdStr = GetUserIdFromProfileUrl(authorNameNode.Attributes["href"].Value);
                    contentAuthorId = Convert.ToInt32(contentAuthorIdStr);

                    createdByName = authorNameNode.InnerText.Trim();
                }

                var article = new TopicArticle
                {
                    Content = contentNode.InnerHtml,
                    CreatedByName = createdByName,
                    CreatedDateTime = contentDateTime,
                    CreatedById = contentAuthorId,
                };
                topicPage.Articles.Add(article);
            }

            var json = JsonSerializer.Serialize<TopicArticlePage>(topicPage);
            await File.WriteAllTextAsync($"{basePath}/topics/{metadata.ForumId}/{metadata.TopicId}-{pageNum}-articles.json", json);
        }

        public static string GetUserIdFromProfileUrl(string profileUrl)
        {
            var createdByUrl = profileUrl;
            var createdByRegex = new Regex("profile\\/([0-9]+)-[^\\/]+\\/", RegexOptions.IgnoreCase);
            var createdByMatch = createdByRegex.Match(createdByUrl);
            if (createdByMatch != null)
            {
                return createdByMatch.Groups[1].Value;
            }
            return string.Empty;
        }

        static async Task GetKSPForumsStructure()
        {
            var topLevelHtml = await FetchURL("https://forum.kerbalspaceprogram.com/");

            var parser = new HtmlDocument
            {
                OptionExtractErrorSourceText = true
            };
            parser.LoadHtml(topLevelHtml);

            var ahrefs = parser.DocumentNode.QuerySelectorAll("#ipsLayout_mainArea > section > ol > li > h2 > a:nth-child(2)");

            foreach (var a in ahrefs)
            {
                var name = a.InnerText.Trim();
                var url = a.Attributes["href"].Value;
                var id = -1;

                var idRegex = new Regex("forum\\/([0-9]+)\\-", RegexOptions.IgnoreCase);
                var regexMatch = idRegex.Match(url);

                var forumIdStr = "1";

                if (regexMatch != null && regexMatch.Groups.Count > 1)
                {
                    forumIdStr = regexMatch.Groups[1].Value;
                    id = Convert.ToInt32(forumIdStr);
                }

                var forumStruct = new ForumStructure { Id = id, Name = name, Url = url };

                forumStruct.Forums = await GetForumsFromParent(forumStruct);

                Forums.Add(forumStruct);
            }

            var json = JsonSerializer.Serialize<List<ForumStructure>>(Forums);

            await File.WriteAllTextAsync($"{basePath}/ksp-forum-def.json", json);
        }

        static async Task<Collection<ForumStructure>> GetForumsFromParent(ForumStructure parent)
        {
            var forumCollection = new Collection<ForumStructure>();

            var topLevelHtml = await FetchURL(parent.Url);

            var parser = new HtmlDocument
            {
                OptionExtractErrorSourceText = true
            };
            parser.LoadHtml(topLevelHtml);

            var forumRows = parser.DocumentNode.QuerySelectorAll("li.cForumRow");

            foreach (var row in forumRows)
            {
                var ahrefNode = row.QuerySelector("h4.ipsDataItem_title a");
                var descNode = row.QuerySelector(".ipsDataItem_meta p");

                var idStr = row.Attributes["data-forumid"].Value;
                var id = Convert.ToInt32(idStr);
                var name = ahrefNode.InnerText.Trim();
                var url = ahrefNode.Attributes["href"].Value;
                var description = descNode?.InnerText?.Trim() ?? string.Empty;

                var forumStruct = new ForumStructure
                {
                    Id = id,
                    Name = name,
                    Url = url,
                    Description = description,
                };

                forumCollection.Add(forumStruct);

                var children = await GetForumsFromParent(forumStruct);
                forumStruct.Forums = children;
            }

            return forumCollection;
        }

        static HttpClient _client;
        static async Task<string> FetchURL(string url, bool firstScrape = false)
        {
            //if(_client == null)
            //{
                var messageHandler = new HttpClientHandler
                {
                    AllowAutoRedirect = true,
                };

                var _client = new HttpClient(messageHandler);
                _client.DefaultRequestVersion = new Version(2, 0);

                _client.DefaultRequestHeaders.Add("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
                _client.DefaultRequestHeaders.Add("referer", url);
                _client.DefaultRequestHeaders.Add("accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.9");

                _client.DefaultRequestHeaders.Add("Accept-Language", "en-GB,en;q=0.9,es;q=0.8");
                _client.DefaultRequestHeaders.Add("Cache-Control", "max-age=0");
                _client.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
                _client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "document");
                _client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "navigate");
                _client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "none");
                _client.DefaultRequestHeaders.Add("Sec-Fetch-User", "?1");
            //}

            HttpResponseMessage response = null;
            try
            {
                response = await _client.GetAsync(url);
                response.EnsureSuccessStatusCode();

                failures = 0;
            }
            catch (Exception ex)
            {
                failures++;
                if (failures > 3)
                {
                    throw;
                }
                return string.Empty;
            }

            return await response.Content.ReadAsStringAsync();
        }

        static async Task<byte[]> FetchFileData(string url, bool firstScrape = false)
        {
            var client = new HttpClient();

            client.DefaultRequestVersion = new Version(2, 0);

            client.DefaultRequestHeaders.Add("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("referer", url);
            client.DefaultRequestHeaders.Add("accept", "*/*");

            client.DefaultRequestHeaders.Add("Accept-Language", "en-GB,en;q=0.9,es;q=0.8");
            client.DefaultRequestHeaders.Add("Cache-Control", "max-age=0");
            client.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "document");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "navigate");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "none");
            client.DefaultRequestHeaders.Add("Sec-Fetch-User", "?1");
            HttpResponseMessage response = null;
            try
            {
                response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();

                failures = 0;
            }
            catch (Exception ex)
            {
                return new byte[0];
            }

            return await response.Content.ReadAsByteArrayAsync();
        }
    }
}
