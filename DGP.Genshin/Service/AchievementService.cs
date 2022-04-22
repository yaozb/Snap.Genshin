﻿using DGP.Genshin.DataModel.Achievement;
using DGP.Genshin.DataModel.Achievement.CocoGoat;
using DGP.Genshin.DataModel.Achievement.UIAF;
using DGP.Genshin.Service.Abstraction.Achievement;
using Newtonsoft.Json;
using Snap.Core.DependencyInjection;
using Snap.Data.Json;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace DGP.Genshin.Service
{
    /// <inheritdoc cref="IAchievementService"/>
    [Service(typeof(IAchievementService), InjectAs.Transient)]
    internal class AchievementService : IAchievementService
    {
        private const string AchievementsFileName = "achievements.json";

        /// <inheritdoc/>
        public List<IdTime> GetCompletedItems()
        {
            return Json.FromFileOrNew<List<IdTime>>(PathContext.Locate(AchievementsFileName));
        }

        /// <inheritdoc/>
        public void SaveCompletedItems(ObservableCollection<Achievement> achievements)
        {
            IEnumerable<IdTime> idTimes = achievements
                .Where(a => a.IsCompleted)
                .Select(a => new IdTime(a.Id, a.CompleteDateTime));
            Json.ToFile(PathContext.Locate(AchievementsFileName), idTimes);
        }

        /// <inheritdoc/>
        public IEnumerable<IdTime>? TryGetImportData(ImportAchievementSource source, string argument)
        {
            try
            {
                switch (source)
                {
                    case ImportAchievementSource.Cocogoat:
                        {
                            CocoGoatUserData? data = Json.FromFile<CocoGoatUserData>(argument);
                            if (data?.Value?.Achievements is List<CocoGoatAchievement> achievements)
                            {
                                return achievements
                                    .Select(a => new IdTime(a.Id, a.Date));
                            }

                            break;
                        }

                    case ImportAchievementSource.UIAF:
                        {
                            UIAF? data = Json.ToObject<UIAF>(argument);
                            if (data?.List is List<UIAFItem> achievements)
                            {
                                return achievements
                                    .Select(a => new IdTime(a.Id, DateTime.UnixEpoch.AddSeconds(a.TimeStamp)));
                            }

                            break;
                        }

                    default:
                        break;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <inheritdoc/>
        public IEnumerable<IdTime>? TryGetImportData(string dataString)
        {
            try
            {
                IEnumerable<IdTimeStamp>? idTimeStamps = Json.ToObject<IEnumerable<IdTimeStamp>>(dataString);
                return idTimeStamps?
                    .Select(ts => new IdTime(ts.Id, DateTime.UnixEpoch.AddSeconds(ts.TimeStamp)));
            }
            catch (JsonReaderException)
            {
                return null;
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (JsonSerializationException)
            {
                return null;
            }
        }
    }
}