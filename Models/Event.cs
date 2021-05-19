using System;
using System.Collections.Generic;

namespace EventsParser.Models
{
    public class EventData
    {
        public EventData()
        {
            IsScheduled = true;
            IsCancelled = false;
        }

        public string EventName { get; set; }
        public DateTime Date { get; set; }
        public string ImgSrc { get; set; }
        public string Venue { get; set; }
        public string Location { get; set; }

        public bool IsScheduled  { get; set; }
        public bool IsCancelled { get; set; }

        public Dictionary<string, List<FightRecord>> FightCard { get; set; }
        public List<string> BonusAwards { get; set; }
    }
}