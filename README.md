# 1Flow SoundPanel

Windows-ის დესკტოპ პროგრამა 1Flow-ის ეკოსისტემისთვის: ხმოვანი ბიბლიოთეკა, სწრაფი ძებნა, ეფექტები,
მინი-პლეერი, ხმოვანი ბრძანებები და ჩამოტვირთვა (yt-dlp).

## მოთხოვნები
- Windows 10/11 (x64). .NET ცალკე დაყენება არ სჭირდება.
- პირველ გაშვებაზე პროგრამა თვითონ ჩამოტვირთავს დამხმარე ხელსაწყოებს (yt-dlp, ffmpeg, ხმოვანი მოდელი).

## აწყობა
```powershell
powershell -ExecutionPolicy Bypass -File build.ps1   # → 1FlowSoundPanel.zip
```

## გამოშვება
1. გაზარდე `<Version>` `SoundPanel.csproj`-ში და დააკომიტე
2. `git tag v1.0.1` → `git push --tags`
3. GitHub Actions თვითონ ააგებს და გამოაქვეყნებს `1FlowSoundPanel.zip`-ს.
