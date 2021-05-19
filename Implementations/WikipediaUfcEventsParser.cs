using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using EventsParser.Models;
using Microsoft.Extensions.Logging;
using HtmlAgilityPack;
using System.Linq;
using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace EventsParser.Implementations
{
    public class WikipediaUfcEventsParser : EventsParser
    {
        private const string Scheduled_Events_Xpath = ".//table[@id='Scheduled_events']/tbody/tr";
        private const string Past_Events_Xpath = ".//table[@id='Past_events']/tbody/tr";
        private const string Event_Img_Xpath = ".//table[contains(@class, 'infobox')]/descendant::img";
        private const string Fight_Card_Xpath = ".//table[contains(@class, 'toccolours')]/tbody/tr";
        private const string Announced_Bouts_Xpath = ".//h2[span[@id='Announced_bouts']]";
        private const string Bonus_Awards_Xpath = ".//h2[span[@id='Bonus_awards']]";
        private const string Cancelled_Token = "Cancelled";
        private const string Base_Uri = "https://en.wikipedia.org";

        public WikipediaUfcEventsParser(HttpClient httpClient, ILogger<WikipediaUfcEventsParser> logger) : base(httpClient, logger)
        {

        }

        public int QuantityOfScheduledEvents { get; set; } = 10;
        public int QuantityOfPastEvents { get; set; } = 5;

        internal async override Task<ICollection<EventData>> ParseEventsFrom(string htmlContent, CancellationToken cancellationToken = default)
        {
            var events = new List<EventData>();
            var documentNode = GetDocumentNode(htmlContent);

            var scheduledEventsSource = documentNode.SelectNodes(Scheduled_Events_Xpath)?.ToList();
            var pastEventsSource = documentNode.SelectNodes(Past_Events_Xpath)?.ToList();

            if (scheduledEventsSource is not null)
            {
                scheduledEventsSource = scheduledEventsSource.Skip(1).TakeLast(QuantityOfScheduledEvents).ToList();
                Log(LogLevel.Debug, $"Starting parsing scheduled events. Esstiminated quantity {scheduledEventsSource.Count}");
                await AddEvents(scheduledEventsSource, events);
            }
            else
            {
                Log(LogLevel.Warning, $"Scheduled events was not found.");
            }

            if (pastEventsSource is not null)
            {
                pastEventsSource = pastEventsSource.Skip(1).Take(QuantityOfPastEvents).ToList();
                Log(LogLevel.Debug, $"Starting parsing past events. Esstiminated quantity {pastEventsSource.Count}");
                await AddEvents(pastEventsSource, events, true);
            }
            else
            {
                Log(LogLevel.Warning, $"Past events was not found.");
            }

            return events;
        }

        private async Task AddEvents(IEnumerable<HtmlNode> source, List<EventData> output, bool isPastEvents = false)
        {
            if (source is null || !source.Any())
                return;

            var previousEventVenue = string.Empty;
            var previousEventLocation = string.Empty;
            var previousEventIsCancelled = false;

            foreach (var node in source)
            {
                var tdNodes = node.SelectNodes($"child::td");

                if (isPastEvents)
                {
                    tdNodes.RemoveAt(tdNodes.Count - 1);
                    tdNodes.RemoveAt(0);
                }

                var eventData = await GetEventDataFrom(tdNodes, previousEventVenue, previousEventLocation, previousEventIsCancelled);

                eventData.IsScheduled = !isPastEvents;

                previousEventVenue = eventData.Venue;
                previousEventLocation = eventData.Location;
                previousEventIsCancelled = eventData.IsCancelled;

                Log(LogLevel.Information, $"Finished parsing event: {eventData.EventName} / {eventData.Date.ToShortDateString()}");
                Log(LogLevel.Debug, $"Additional Information about the event: Venue - {eventData.Venue}; Location - {eventData.Location}; FightCard.TotalCards: {eventData.FightCard.Keys.Count}; FightCard.TotalFights: {eventData.FightCard.Values.Sum(list => list.Count)};");

                output.Add(eventData);
            }
        }

        private async Task<EventData> GetEventDataFrom(HtmlNodeCollection tdNodes, string previousEventVenue, string previousEventLocation, bool previousEventIsCancelled, CancellationToken cancellationToken = default)
        {
            var eventNameIndex = 0;
            var eventDateIndex = 1;
            var eventVenueIndex = 2;
            var eventLocationIndex = 3;
            var eventIsCancelledIndex = 4;

            var eventName = GetInnerTextFrom(tdNodes[eventNameIndex]);
            if (string.IsNullOrWhiteSpace(eventName))
            {
                Log(LogLevel.Warning, $"Event name was null or white space: {eventName}");
            }
            else
            {
                Log(LogLevel.Information, $"Start parsing event: {eventName}");
            }

            var eventDate = GetEventDate(tdNodes, eventDateIndex);

            if (tdNodes.ElementAtOrDefault(eventVenueIndex) is not null)
            {
                var currentEventVenue = GetInnerTextFrom(tdNodes[eventVenueIndex]);

                if (!Regex.IsMatch(currentEventVenue, @"^&#91;\d*&#93;"))
                    previousEventVenue = currentEventVenue;
            }

            if (tdNodes.ElementAtOrDefault(eventLocationIndex) is not null)
            {
                var currentEventLocation = GetInnerTextFrom(tdNodes[eventLocationIndex]);

                if (!Regex.IsMatch(currentEventLocation, @"^&#91;\d*&#93;"))
                    previousEventLocation = currentEventLocation;
            }

            if (tdNodes.ElementAtOrDefault(eventIsCancelledIndex) is not null)
                previousEventIsCancelled = GetInnerTextFrom(tdNodes[eventIsCancelledIndex]).Contains(Cancelled_Token);

            var eventData = new EventData()
            {
                EventName = eventName,
                Date = eventDate,
                Venue = previousEventVenue,
                Location = previousEventLocation,
                IsCancelled = previousEventIsCancelled
            };

            var fightCardHref = GetFightCardHref(tdNodes[eventNameIndex]);

            if (string.IsNullOrWhiteSpace(fightCardHref))
            {
                Log(LogLevel.Debug, $"Fight card href for event: {eventName} / {eventDate.ToShortDateString()} was not found.");
                return eventData;
            }

            Log(LogLevel.Information, $"Start parsing event deatails for: {eventName} / {eventDate.ToShortDateString()}.");
            var fightCardUri = new Uri(Base_Uri + fightCardHref);
            var documentNode = GetDocumentNode(await GetHtmlContentFromWebAsync(fightCardUri, cancellationToken));

            eventData.ImgSrc = GetImgSrc(documentNode);
            eventData.FightCard = GetFightCard(documentNode);

            var bonusAwardsNode = documentNode.SelectNodes(Bonus_Awards_Xpath)?.FirstOrDefault();
            if (bonusAwardsNode is not null)
                eventData.BonusAwards = GetBonusAwards(bonusAwardsNode);

            return eventData;
        }

        private List<string> GetBonusAwards(HtmlNode node)
        {
            var result = new List<string>();

            var ul = node.NextSibling?.NextSibling?.NextSibling?.NextSibling;
            if (ul is not null && ul.Name == "ul")
            {
                var li = ul.ChildNodes?.Where(node => node.NodeType == HtmlNodeType.Element).ToList();
                if (li is not null && li.Any())
                {
                    foreach (var item in li)
                    {
                        var sup = item.ChildNodes.Where(node => node.Name == "sup").FirstOrDefault();
                        if (sup is not null)
                            item.ChildNodes.Remove(sup);

                        var text = GetInnerTextFrom(item);
                        if (text.Length > 20)
                            result.Add(text);
                    }
                }
            }

            return result;
        }

        private Dictionary<string, List<FightRecord>> GetFightCard(HtmlNode document)
        {
            var result = new Dictionary<string, List<FightRecord>>();

            var rows = document.SelectNodes(Fight_Card_Xpath);

            if (rows is not null && rows.Any())
            {
                rows.RemoveAt(1);

                string key = string.Empty;
                var value = new List<FightRecord>();

                foreach (var row in rows)
                {
                    var childs = row.ChildNodes.Where(node => node.NodeType == HtmlNodeType.Element).ToList();

                    if (childs.Count == 1 && childs[0].Name == "th")
                    {
                        key = childs[0].InnerText.Trim();

                        if (result.ContainsKey(key))
                        {
                            Log(LogLevel.Debug, $"Сontains non-unique fight cards. Non-unique key: {key}");
                            return new Dictionary<string, List<FightRecord>>();
                        }

                        result.Add(key, new List<FightRecord>());
                        continue;
                    }

                    var fightRecord = new FightRecord()
                    {
                        WeightClass = GetInnerTextFrom(childs[0]),
                        FirtsFighter = GetInnerTextFrom(childs[1]),
                        SecondFighter = GetInnerTextFrom(childs[3]),
                        Method = GetInnerTextFrom(childs[4]),
                        Round = GetInnerTextFrom(childs[5]),
                        Time = GetInnerTextFrom(childs[6])
                    };

                    result[key].Add(fightRecord);
                }
            }

            var announcedBoutsNode = document.SelectNodes(Announced_Bouts_Xpath)?.FirstOrDefault();
            if (announcedBoutsNode is not null)
                AddAnnouncedBouts(announcedBoutsNode, result);

            return result;
        }

        private void AddAnnouncedBouts(HtmlNode node, Dictionary<string, List<FightRecord>> result)
        {
            var ul = node.NextSibling?.NextSibling;

            if (ul is not null && ul.Name == "ul")
            {
                var li = ul.ChildNodes?.Where(node => node.NodeType == HtmlNodeType.Element).ToList();
                if (li is not null && li.Any())
                {
                    var key = "Announced bouts";

                    if (result.ContainsKey(key))
                    {
                        Log(LogLevel.Debug, $"Сontains non-unique fight cards. Non-unique key: {key}");
                        return;
                    }

                    result.Add(key, new List<FightRecord>());

                    foreach (var item in li)
                    {
                        var sup = item.ChildNodes.Where(node => node.Name == "sup").FirstOrDefault();
                        if (sup is not null)
                            item.ChildNodes.Remove(sup);
                        var text = GetInnerTextFrom(item);
                        var splited = text.Split(':');
                        var fighters = splited[1].Split("vs.");

                        var fightRecord = new FightRecord()
                        {
                            WeightClass = splited[0].Replace("bout", string.Empty).Trim(),
                            FirtsFighter = fighters[0].Trim(),
                            SecondFighter = fighters[1].Trim(),
                            Method = string.Empty,
                            Round = string.Empty,
                            Time = string.Empty
                        };

                        result[key].Add(fightRecord);
                    }
                }
            }
        }

        private DateTime GetEventDate(HtmlNodeCollection tdNodes, int eventDateIndex)
        {
            var dateString = GetInnerTextFrom(tdNodes[eventDateIndex]);
            try
            {
                return DateTime.Parse(dateString, CultureInfo.InvariantCulture);
            }
            catch (ArgumentNullException exception)
            {
                Log(LogLevel.Error, "DateTime string was null.", exception);
                throw;
            }
            catch (FormatException exception)
            {
                Log(LogLevel.Error, $"DateTime string does not contain a valid representation of a date and time: {dateString}", exception);
                throw;
            }
        }

        private HtmlNode GetDocumentNode(string htmlContent)
        {
            var html = new HtmlDocument();
            html.LoadHtml(htmlContent);
            return html.DocumentNode;
        }

        private static string GetFightCardHref(HtmlNode node)
        {
            return node.SelectSingleNode("descendant::a[@href]")?.GetAttributeValue<string>("href", default);
        }

        private static string GetImgSrc(HtmlNode node)
        {
            return node.SelectSingleNode(Event_Img_Xpath)?.GetAttributeValue("src", string.Empty) ?? string.Empty;
        }

        private string GetInnerTextFrom(HtmlNode htmlNode)
        {
            var text = new StringBuilder();

            foreach (var child in htmlNode.ChildNodes)
                text.Append(child.InnerText);

            return text.ToString().Trim();
        }
    }
}