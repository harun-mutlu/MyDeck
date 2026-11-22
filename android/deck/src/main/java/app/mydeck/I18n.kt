package app.mydeck

import java.util.Locale

object I18n {
    @Volatile var lang: String = if (Locale.getDefault().language == "tr") "tr" else "en"

    fun t(en: String, vararg args: Any?): String {
        var s = if (lang == "tr") Turkish.map[en] ?: en else en
        args.forEachIndexed { i, a -> s = s.replace("{$i}", a?.toString() ?: "") }
        return s
    }
}
