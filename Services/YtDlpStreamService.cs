// Command dasar
        startInfo.ArgumentList.Add("--no-warnings");
        startInfo.ArgumentList.Add("--no-cache-dir");
        startInfo.ArgumentList.Add("--newline");
        startInfo.ArgumentList.Add("--ignore-config");
        startInfo.ArgumentList.Add("--force-overwrites");

        // --- STRATEGI SURVIVAL (GEO-BYPASS & PROXY) ---
        startInfo.ArgumentList.Add("--geo-bypass");
        startInfo.ArgumentList.Add("--geo-bypass-country");
        startInfo.ArgumentList.Add("US");

        // Ganti dengan IP Proxy HTTP yang aktif jika masih gagal
        // startInfo.ArgumentList.Add("--proxy");
        // startInfo.ArgumentList.Add("http://IP_PROXY_ANDA:PORT");

        // User-Agent & Referer
        startInfo.ArgumentList.Add("--user-agent");
        startInfo.ArgumentList.Add("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36");
        startInfo.ArgumentList.Add("--referer");
        startInfo.ArgumentList.Add("https://www.youtube.com/");

        // Client iOS
        startInfo.ArgumentList.Add("--extractor-args");
        startInfo.ArgumentList.Add("youtube:player_client=ios");
