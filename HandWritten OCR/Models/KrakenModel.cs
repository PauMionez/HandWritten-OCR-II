namespace HandWritten_OCR.Models;

public enum KrakenModel
{
    McCatmus,          // mccatmus_v1.mlmodel — 7 languages (IT/LA/FR/ES/EN/DE/OC) 16th-21st c. handwritten
    TridisEarlyModern, // tridis_v2_medieval_earlymodern.mlmodel — multilingual 11th-16th c. parish/legal records
    CatmusMedieval,    // catmus-print-fondue-large.mlmodel — medieval French/Italian (12th-15th c.)
    LectaurepFrench    // lectaurep_base.mlmodel — modern French documents (19th-20th c.)
}
