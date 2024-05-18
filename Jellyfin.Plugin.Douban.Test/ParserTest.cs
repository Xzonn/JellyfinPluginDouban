using CsvHelper;
using CsvHelper.Configuration;
using MediaBrowser.Controller.Providers;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace Jellyfin.Plugin.Douban.Test
{
    public class ParserTest
    {
        [Fact]
        public void SeasonIndexTest()
        {
            using var reader = new StreamReader("data/season_index.csv");
            using var csvReader = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                MissingFieldFound = null,
                HeaderValidated = null,
            });
            var records = csvReader.GetRecords<SeasonIndex>().Select(_ =>
            {
                var guessed = Helper.GuessSeasonIndex(new ItemLookupInfo
                {
                    Name = _.Title,
                    Path = _.Title,
                });
                _.GuessedIndex = guessed;
                _.Same = _.Index == guessed;
                return _;
            });

            using var writer = new StreamWriter(File.Create("data/season_index_output.csv"), new UTF8Encoding(true));
            using var csvWriter = new CsvWriter(writer, CultureInfo.InvariantCulture);
            csvWriter.WriteRecords(records);
        }

        class SeasonIndex
        {
            public string Title { get; set; }
            public int? Index { get; set; }
            public int? GuessedIndex { get; set; }
            public bool? Same { get; set; }
        }
    }
}
