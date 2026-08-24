using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Domain.Rules;

namespace NeyraOptimizer.Optimization.Catalog;

public static class RulesCatalog
{
    public const int CurrentCatalogVersion = 1;

    public static IReadOnlyList<RuleDefinition> GetAllRules()
    {
        var rules = new List<RuleDefinition>();

        // 1. Services
        rules.Add(new RuleDefinition
        {
            RuleId = "service_diagtrack",
            Name = "Nonaktifkan Telemetri Windows (DiagTrack)",
            Description = "Menonaktifkan layanan Connected User Experiences and Telemetry untuk mengurangi background CPU dan disk I/O.",
            Rationale = "Layanan ini terus berjalan di latar belakang mengumpulkan dan mengirim telemetri sistem.",
            Area = RuleArea.Services,
            Category = RecommendationCategory.Safe,
            RiskLevel = RiskLevel.Safe,
            RequiresAdministrator = true,
            RequiresRestart = false,
            RollbackDescription = "Mengubah tipe startup layanan DiagTrack kembali ke Automatic.",
            Payload = new Dictionary<string, string> { ["TargetId"] = "DiagTrack" }
        });

        rules.Add(new RuleDefinition
        {
            RuleId = "service_dmwappushservice",
            Name = "Nonaktifkan WAP Push Routing Service",
            Description = "Menonaktifkan layanan routing pesan telemetri WAP push yang tidak dibutuhkan oleh pengguna umum.",
            Rationale = "Komponen tambahan untuk pengiriman data telemetri diagnostik perusahaan.",
            Area = RuleArea.Services,
            Category = RecommendationCategory.Safe,
            RiskLevel = RiskLevel.Safe,
            RequiresAdministrator = true,
            RequiresRestart = false,
            RollbackDescription = "Mengembalikan tipe startup layanan ke Manual.",
            Payload = new Dictionary<string, string> { ["TargetId"] = "dmwappushservice" }
        });

        rules.Add(new RuleDefinition
        {
            RuleId = "service_sysmain",
            Name = "Optimalkan SysMain (SuperFetch) pada Media SSD",
            Description = "Mengubah startup SysMain menjadi Manual pada perangkat dengan SSD.",
            Rationale = "Media SSD memiliki kecepatan akses acak sangat tinggi sehingga caching agresif ke RAM tidak lagi diperlukan.",
            Area = RuleArea.Services,
            Category = RecommendationCategory.Recommended,
            RiskLevel = RiskLevel.Low,
            RequiresAdministrator = true,
            RequiresRestart = false,
            RollbackDescription = "Mengembalikan tipe startup layanan SysMain ke Automatic.",
            Payload = new Dictionary<string, string> { ["TargetId"] = "SysMain" }
        });

        rules.Add(new RuleDefinition
        {
            RuleId = "service_retaildemo",
            Name = "Nonaktifkan Layanan Retail Demo",
            Description = "Layanan demonstrasi toko yang tidak diperlukan pada komputer pribadi atau kerja.",
            Rationale = "Hanya digunakan oleh toko display elektronik.",
            Area = RuleArea.Services,
            Category = RecommendationCategory.Safe,
            RiskLevel = RiskLevel.Safe,
            RequiresAdministrator = true,
            RequiresRestart = false,
            RollbackDescription = "Mengembalikan tipe startup layanan ke Manual.",
            Payload = new Dictionary<string, string> { ["TargetId"] = "RetailDemo" }
        });

        rules.Add(new RuleDefinition
        {
            RuleId = "service_remoteregistry",
            Name = "Nonaktifkan Remote Registry (Peningkatan Keamanan)",
            Description = "Mencegah akses manipulasi registri dari jaringan jarak jauh untuk meningkatkan keamanan dan menghemat resource.",
            Rationale = "Mencegah celah manipulasi registri jarak jauh.",
            Area = RuleArea.Services,
            Category = RecommendationCategory.Safe,
            RiskLevel = RiskLevel.Safe,
            RequiresAdministrator = true,
            RequiresRestart = false,
            RollbackDescription = "Mengembalikan tipe startup layanan ke Disabled.",
            Payload = new Dictionary<string, string> { ["TargetId"] = "RemoteRegistry" }
        });

        rules.Add(new RuleDefinition
        {
            RuleId = "service_maps",
            Name = "Nonaktifkan Downloaded Maps Manager",
            Description = "Menonaktifkan background sync untuk peta offline Windows jika Anda tidak menggunakan aplikasi Windows Maps.",
            Rationale = "Menghemat siklus CPU dan bandwidth latar belakang.",
            Area = RuleArea.Services,
            Category = RecommendationCategory.Safe,
            RiskLevel = RiskLevel.Safe,
            RequiresAdministrator = true,
            RequiresRestart = false,
            RollbackDescription = "Mengembalikan tipe startup layanan ke Automatic.",
            Payload = new Dictionary<string, string> { ["TargetId"] = "MapsBroker" }
        });

        // 2. Scheduled Tasks
        rules.Add(new RuleDefinition
        {
            RuleId = "task_ceip_consolidator",
            Name = "Nonaktifkan Task Telemetri CEIP Consolidator",
            Description = "Menonaktifkan tugas terjadwal pengumpul data Customer Experience Improvement Program.",
            Rationale = "Menghindari spike disk dan CPU terjadwal saat pengumpulan metrik.",
            Area = RuleArea.ScheduledTasks,
            Category = RecommendationCategory.Safe,
            RiskLevel = RiskLevel.Safe,
            RequiresAdministrator = true,
            RequiresRestart = false,
            RollbackDescription = "Mengaktifkan kembali task Consolidator pada Task Scheduler.",
            Payload = new Dictionary<string, string> { ["TargetId"] = @"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator" }
        });

        rules.Add(new RuleDefinition
        {
            RuleId = "task_ceip_usb",
            Name = "Nonaktifkan Task Telemetri UsbCeip",
            Description = "Menonaktifkan pengiriman data diagnostik perangkat USB ke server Microsoft.",
            Rationale = "Mengurangi aktivitas background terjadwal yang tidak esensial.",
            Area = RuleArea.ScheduledTasks,
            Category = RecommendationCategory.Safe,
            RiskLevel = RiskLevel.Safe,
            RequiresAdministrator = true,
            RequiresRestart = false,
            RollbackDescription = "Mengaktifkan kembali task UsbCeip pada Task Scheduler.",
            Payload = new Dictionary<string, string> { ["TargetId"] = @"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip" }
        });

        rules.Add(new RuleDefinition
        {
            RuleId = "task_appexp_compattel",
            Name = "Nonaktifkan Microsoft Compatibility Appraiser",
            Description = "Menghentikan tugas terjadwal yang sering memicu penggunaan CPU & Disk tinggi di background saat evaluasi kompatibilitas aplikasi.",
            Rationale = "Sering memicu 100% disk usage pada komputer low-end.",
            Area = RuleArea.ScheduledTasks,
            Category = RecommendationCategory.Recommended,
            RiskLevel = RiskLevel.Safe,
            RequiresAdministrator = true,
            RequiresRestart = false,
            RollbackDescription = "Mengaktifkan kembali task Microsoft Compatibility Appraiser.",
            Payload = new Dictionary<string, string> { ["TargetId"] = @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser" }
        });

        // 3. Visual Effects
        rules.Add(new RuleDefinition
        {
            RuleId = "visual_animate_windows",
            Name = "Nonaktifkan Animasi Minimize & Maximize Jendela",
            Description = "Meniadakan jeda animasi saat membuka/menutup jendela agar UI terasa lebih responsif pada hardware terbatas.",
            Rationale = "Meningkatkan responsivitas visual jendela tanpa membebani GPU.",
            Area = RuleArea.VisualEffects,
            Category = RecommendationCategory.Recommended,
            RiskLevel = RiskLevel.Safe,
            RequiresAdministrator = false,
            RequiresRestart = false,
            RollbackDescription = "Mengaktifkan kembali animasi minimize/maximize di System Properties.",
            Payload = new Dictionary<string, string> { ["TargetId"] = "MinAnimate" }
        });

        rules.Add(new RuleDefinition
        {
            RuleId = "visual_taskbar_animations",
            Name = "Nonaktifkan Animasi Taskbar",
            Description = "Mengurangi beban GPU render pada taskbar Windows 10 & 11.",
            Rationale = "Meringankan taskbar rendering pada integrated graphics.",
            Area = RuleArea.VisualEffects,
            Category = RecommendationCategory.Recommended,
            RiskLevel = RiskLevel.Safe,
            RequiresAdministrator = false,
            RequiresRestart = false,
            RollbackDescription = "Mengaktifkan kembali animasi taskbar.",
            Payload = new Dictionary<string, string> { ["TargetId"] = "TaskbarAnimations" }
        });

        rules.Add(new RuleDefinition
        {
            RuleId = "visual_menu_fade",
            Name = "Nonaktifkan Efek Fade / Slide pada Menu",
            Description = "Membuat klik kanan dan menu drop-down muncul instan tanpa efek fade animasi.",
            Rationale = "Menu konteks muncul seketika saat diklik.",
            Area = RuleArea.VisualEffects,
            Category = RecommendationCategory.Recommended,
            RiskLevel = RiskLevel.Safe,
            RequiresAdministrator = false,
            RequiresRestart = false,
            RollbackDescription = "Mengaktifkan kembali efek fade menu.",
            Payload = new Dictionary<string, string> { ["TargetId"] = "MenuAnimation" }
        });

        // 4. Privacy & Telemetry
        rules.Add(new RuleDefinition
        {
            RuleId = "privacy_advertising_id",
            Name = "Nonaktifkan Advertising ID Pelacak Iklan",
            Description = "Mencegah aplikasi menggunakan pengenal iklan unik untuk memantau preferensi pengguna.",
            Rationale = "Meningkatkan privasi data pengguna.",
            Area = RuleArea.Privacy,
            Category = RecommendationCategory.Safe,
            RiskLevel = RiskLevel.Safe,
            RequiresAdministrator = false,
            RequiresRestart = false,
            RollbackDescription = "Mengubah nilai registri kembali ke 1.",
            Payload = new Dictionary<string, string> { ["TargetId"] = @"HKCU\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo\Enabled" }
        });

        rules.Add(new RuleDefinition
        {
            RuleId = "privacy_tailored_experiences",
            Name = "Nonaktifkan Tailored Experiences (Iklan Diagnostik)",
            Description = "Menonaktifkan saran dan penawaran Microsoft berbasis data diagnostik komputer.",
            Rationale = "Meniadakan iklan dan rekomendasi produk di berbagai area Windows.",
            Area = RuleArea.Privacy,
            Category = RecommendationCategory.Safe,
            RiskLevel = RiskLevel.Safe,
            RequiresAdministrator = false,
            RequiresRestart = false,
            RollbackDescription = "Mengubah nilai registri kembali ke 1.",
            Payload = new Dictionary<string, string> { ["TargetId"] = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Privacy\TailoredExperiencesWithDiagnosticDataEnabled" }
        });

        rules.Add(new RuleDefinition
        {
            RuleId = "privacy_web_search",
            Name = "Nonaktifkan Hasil Web Bing pada Start Menu Search",
            Description = "Mempercepat pencarian Start Menu dengan hanya mencari berkas dan aplikasi lokal tanpa mengirim query ke Bing.",
            Rationale = "Pencarian Start Menu menjadi instan dan tidak memakan bandwidth internet.",
            Area = RuleArea.Privacy,
            Category = RecommendationCategory.Recommended,
            RiskLevel = RiskLevel.Safe,
            RequiresAdministrator = false,
            RequiresRestart = false,
            RollbackDescription = "Menghapus policy registri DisableSearchBoxSuggestions.",
            Payload = new Dictionary<string, string> { ["TargetId"] = @"HKCU\Software\Policies\Microsoft\Windows\Explorer\DisableSearchBoxSuggestions" }
        });

        // 5. Power & Gaming
        rules.Add(new RuleDefinition
        {
            RuleId = "power_game_mode",
            Name = "Aktifkan Windows Game Mode",
            Description = "Menginstruksikan Windows untuk memprioritaskan alokasi CPU dan GPU pada game aktif serta menunda background updates.",
            Rationale = "Menjaga stabilitas framerate (FPS) dan frame pacing saat bermain game.",
            Area = RuleArea.Power,
            Category = RecommendationCategory.Recommended,
            RiskLevel = RiskLevel.Safe,
            RequiresAdministrator = false,
            RequiresRestart = false,
            RollbackDescription = "Mengubah nilai registri AutoGameModeEnabled kembali ke 0.",
            Payload = new Dictionary<string, string> { ["TargetId"] = @"HKCU\Software\Microsoft\GameBar\AutoGameModeEnabled" }
        });

        return rules;
    }
}