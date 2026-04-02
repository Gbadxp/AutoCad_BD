using System.Collections.Generic;

namespace FiberPlugin.Models
{
    public class CableModel
    {
        public string FullName { get; set; }
        public string ShortName { get; set; }
        public double WeightKgKm { get; set; }

        public override string ToString()
        {
            return FullName; // Para exibir no ComboBox do formulário
        }
    }

    public static class CableProvider
    {
        public static List<CableModel> GetCables()
        {
            return new List<CableModel>
            {
                new CableModel { FullName = "CFOA-SM-AS-80-S-06 FO", ShortName = "ASU-80 06F.O", WeightKgKm = 31 },
                new CableModel { FullName = "CFOA-SM-AS-120-S-06 FO", ShortName = "ASU-120 06F.O", WeightKgKm = 34 },
                new CableModel { FullName = "CFOA-SM-AS-200-S-06 FO", ShortName = "ASU-200 06F.O", WeightKgKm = 38 },
                new CableModel { FullName = "CFOA-SM-AS-80-S-12 FO", ShortName = "ASU-80 12F.O", WeightKgKm = 31 },
                new CableModel { FullName = "CFOA-SM-AS-120-S-12 FO", ShortName = "ASU-120 12F.O", WeightKgKm = 34 },
                new CableModel { FullName = "CFOA-SM-AS-200-S-12 FO", ShortName = "ASU-200 12F.O", WeightKgKm = 38 },
                new CableModel { FullName = "CFOA-SM-AS-80-S-24 FO", ShortName = "ASU-80 24F.O", WeightKgKm = 33 },
                new CableModel { FullName = "CFOA-SM-AS-120-S-24 FO", ShortName = "ASU-120 24F.O", WeightKgKm = 46 },
                new CableModel { FullName = "CFOA-SM-AS-200-S-24 FO", ShortName = "ASU-200 24F.O", WeightKgKm = 63 }
            };
        }
    }
}
