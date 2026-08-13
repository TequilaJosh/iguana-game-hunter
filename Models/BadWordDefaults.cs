namespace GameTracker.Models
{
    /// <summary>
    /// The starter profanity list the TTS bad-word filter is seeded with. Copied into
    /// the user's editable <see cref="ChatTtsSettings.BadWords"/> on first run, so the
    /// streamer can see, edit, add to, or remove any of these. Matched as whole words
    /// (case-insensitive), which is why inflections are spelled out here.
    /// </summary>
    public static class BadWordDefaults
    {
        public static readonly string[] Words =
        {
            // f-word family
            "fuck", "fucks", "fucked", "fucker", "fuckers", "fucking", "fuckin",
            "motherfucker", "motherfuckers", "motherfucking", "motherfuckin",
            "clusterfuck", "fuckface", "fuckhead", "fuckwit", "fucktard", "fuckboy", "fuckboi",
            // s-word family
            "shit", "shits", "shitted", "shitting", "shitter", "shitty", "shithead",
            "shithole", "shitface", "shitshow", "bullshit", "dipshit", "dogshit",
            "batshit", "horseshit", "apeshit",
            // bitch
            "bitch", "bitches", "bitchy", "bitching", "bitchin", "sonofabitch", "sonuvabitch",
            // ass
            "ass", "asses", "asshole", "assholes", "asshat", "asshats", "asswipe",
            "assclown", "jackass", "jackasses", "dumbass", "dumbasses", "smartass",
            // c-words
            "cunt", "cunts", "cunty", "cock", "cocks", "cocksucker", "cocksuckers",
            "cockhead", "dick", "dicks", "dickhead", "dickheads", "dickwad", "dickface",
            // p-words
            "piss", "pissed", "pisses", "pissing", "pisser", "pussy", "pussies",
            "prick", "pricks",
            // misc profanity
            "bastard", "bastards", "slut", "sluts", "slutty", "whore", "whores", "whoring",
            "hoe", "hoes", "douche", "douches", "douchebag", "douchebags",
            "wank", "wanker", "wankers", "wanking", "bollocks", "twat", "twats",
            "tits", "titties", "boner", "boners", "cum", "cums", "cumming", "jizz",
            "blowjob", "blowjobs", "handjob", "handjobs", "jerkoff",
            // slurs
            "nigger", "niggers", "nigga", "niggas", "niggaz",
            "faggot", "faggots", "fag", "fags", "faggy", "dyke", "dykes",
            "tranny", "trannies", "retard", "retards", "retarded",
            "spic", "spics", "chink", "chinks", "kike", "kikes", "gook", "gooks",
            "wetback", "wetbacks", "beaner", "beaners", "coon", "coons",
        };
    }
}
