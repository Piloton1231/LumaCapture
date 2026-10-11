using System.Windows;
using System.Windows.Controls;
using System.Globalization;
using LumaCapture.Models;

namespace LumaCapture.Infrastructure;

public static class LocalizationService
{
    private sealed record Phrase(string Japanese, string English, string Korean, string ChineseSimplified, string ChineseTraditional);

    private static readonly Phrase[] Phrases =
    {
        new("処理中", "Working…", "처리 중", "处理中", "處理中"),
        new("未起動", "Not running", "실행 중 아님", "未运行", "未執行"),
        new("アプリ未選択でも保存できます。その場合、PC・アプリ音声は収録せず、選択済みのマイクのみ収録します。", "You can save without selecting apps. No PC or app audio is captured; only a selected microphone is recorded.", "앱을 선택하지 않아도 저장할 수 있습니다. PC·앱 오디오는 녹음하지 않으며 선택한 마이크만 녹음합니다.", "未选择应用也可保存。此时不录制电脑或应用音频，仅录制已选择的麦克风。", "未選擇應用程式也可儲存。此時不錄製電腦或應用程式音訊，僅錄製已選取的麥克風。"),
        new("録音対象のアプリがないため、PC・アプリ音声は収録しません。マイクは選択済みの場合のみ収録します。", "No audio apps are available. PC/app audio will not be recorded; a selected microphone can still be recorded.", "녹음할 앱이 없습니다. PC·앱 오디오는 녹음하지 않으며 선택한 마이크는 녹음할 수 있습니다.", "没有可录音的应用。不会录制电脑或应用音频，但仍可录制已选择的麦克风。", "沒有可錄音的應用程式。不會錄製電腦或應用程式音訊，但仍可錄製已選取的麥克風。"),
        new("アプリ名で検索", "Search by app name", "앱 이름으로 검색", "按应用名称搜索", "依應用程式名稱搜尋"),
        new("メイン画面", "Main display", "주 화면", "主屏幕", "主螢幕"),
        new("すべての画面", "All displays", "모든 화면", "所有屏幕", "所有螢幕"),
        new("LumaCapture 設定", "LumaCapture Settings", "LumaCapture 설정", "LumaCapture 设置", "LumaCapture 設定"),
        new("保存・録画", "Save & Record", "저장 및 녹화", "保存和录制", "儲存與錄製"),
        new("音声", "Audio", "오디오", "音频", "音訊"),
        new("クリップ", "Clips", "클립", "剪辑", "剪輯"),
        new("ホットキー", "Hotkeys", "단축키", "快捷键", "快速鍵"),
        new("情報", "About", "정보", "关于", "關於"),
        new("動画の保存先", "Video save location", "동영상 저장 위치", "视频保存位置", "影片儲存位置"),
        new("通常録画・クリップ・分離音声をこのフォルダーへ保存します。", "Recordings, clips, and separate audio are saved in this folder.", "녹화, 클립 및 분리된 오디오를 이 폴더에 저장합니다.", "录制、剪辑和分离音频将保存到此文件夹。", "錄製、剪輯和分離音訊將儲存到此資料夾。"),
        new("選択", "Browse", "찾아보기", "选择", "選擇"),
        new("開く", "Open", "열기", "打开", "開啟"),
        new("録画", "Recording", "녹화", "录制", "錄製"),
        new("録画する画面", "Capture target", "녹화 대상", "录制目标", "錄製目標"),
        new("録画解像度", "Recording resolution", "녹화 해상도", "录制分辨率", "錄製解析度"),
        new("デフォルト画質", "Default quality", "기본 화질", "默认画质", "預設畫質"),
        new("元の解像度", "Original resolution", "원본 해상도", "原始分辨率", "原始解析度"),
        new("2160p（4K相当）", "2160p (4K)", "2160p(4K)", "2160p（4K）", "2160p（4K）"),
        new("1080p（デフォルト）", "1080p (default)", "1080p(기본)", "1080p（默认）", "1080p（預設）"),
        new("高画質（60 FPS）", "High quality (60 FPS)", "고화질(60 FPS)", "高画质（60 FPS）", "高畫質（60 FPS）"),
        new("標準（30 FPS）", "Standard (30 FPS)", "표준(30 FPS)", "标准（30 FPS）", "標準（30 FPS）"),
        new("軽量（30 FPS）", "Compact (30 FPS)", "경량(30 FPS)", "轻量（30 FPS）", "輕量（30 FPS）"),
        new("マウスカーソルも録画する", "Record the mouse cursor", "마우스 커서도 녹화", "录制鼠标指针", "錄製滑鼠游標"),
        new("録画中、左上に ● REC を表示する（動画には映りません）", "Show ● REC at top left while recording (not included in video)", "녹화 중 왼쪽 위에 ● REC 표시(영상에는 포함되지 않음)", "录制时在左上角显示 ● REC（不会录入视频）", "錄製時在左上角顯示 ● REC（不會錄入影片）"),
        new("録画ボタンを常に表示する", "Always show the recording controls", "녹화 버튼 항상 표시", "始终显示录制按钮", "永遠顯示錄製按鈕"),
        new("常駐", "Background", "백그라운드", "后台", "背景執行"),
        new("Windowsへのサインイン時に自動起動する", "Start when I sign in to Windows", "Windows 로그인 시 자동 시작", "登录 Windows 时自动启动", "登入 Windows 時自動啟動"),
        new("容量保護", "Storage protection", "저장 공간 보호", "存储保护", "儲存空間保護"),
        new("空き容量が少なくなったら通知し、危険な水準では録画を自動停止します。", "Warn when storage is low and automatically stop at a critical level.", "저장 공간이 부족하면 알리고 위험 수준에서 자동으로 녹화를 중지합니다.", "存储空间不足时通知，并在达到危险水平时自动停止录制。", "儲存空間不足時通知，並在達到危險水準時自動停止錄製。"),
        new("通知する空き容量", "Low-space warning", "저장 공간 경고", "低空间警告", "低空間警告"),
        new("自動停止する空き容量", "Auto-stop level", "자동 중지 기준", "自动停止阈值", "自動停止門檻"),
        new("録音する音", "Audio source", "오디오 소스", "音频来源", "音訊來源"),
        new("PC全体の音、または選んだアプリだけの音を録音できます。", "Record all PC audio or only selected applications.", "PC 전체 오디오 또는 선택한 앱만 녹음할 수 있습니다.", "可录制所有电脑音频或仅录制所选应用。", "可錄製所有電腦音訊或僅錄製所選應用程式。"),
        new("PC内の音をすべて録音", "Record all PC audio", "PC의 모든 오디오 녹음", "录制所有电脑音频", "錄製所有電腦音訊"),
        new("選択したアプリだけ録音", "Record selected applications only", "선택한 앱만 녹음", "仅录制所选应用", "僅錄製所選應用程式"),
        new("マイク", "Microphone", "마이크", "麦克风", "麥克風"),
        new("使用する入力デバイスを選択します。マイク音声は自動的に中央から聞こえるよう補正します。", "Choose an input device. Microphone audio is automatically centered.", "입력 장치를 선택합니다. 마이크 오디오는 자동으로 중앙에 맞춰집니다.", "选择输入设备。麦克风音频会自动居中。", "選擇輸入裝置。麥克風音訊會自動置中。"),
        new("一覧を更新", "Refresh list", "목록 새로 고침", "刷新列表", "重新整理清單"),
        new("動画と音声の保存方法", "Video and audio output", "동영상 및 오디오 저장", "视频和音频保存方式", "影片與音訊儲存方式"),
        new("通常はMP4に音声を入れます。必要なときだけWAVへ分離できます。", "Audio is normally embedded in MP4. You can save WAV files separately when needed.", "기본적으로 오디오는 MP4에 포함됩니다. 필요하면 WAV로 분리할 수 있습니다.", "音频通常嵌入 MP4，需要时可单独保存为 WAV。", "音訊通常嵌入 MP4，需要時可單獨儲存為 WAV。"),
        new("動画に音声を入れる（デフォルト）", "Embed audio in video (default)", "동영상에 오디오 포함(기본)", "将音频嵌入视频（默认）", "將音訊嵌入影片（預設）"),
        new("動画 + まとめた音声.wav", "Video + MixedAudio.wav", "동영상 + MixedAudio.wav", "视频 + MixedAudio.wav", "影片 + MixedAudio.wav"),
        new("動画 + アプリごとの音声.wav", "Video + separate audio files", "동영상 + 앱별 오디오 파일", "视频 + 各应用音频文件", "影片 + 各應用程式音訊檔"),
        new("録音するアプリ（複数選択可）", "Applications to record (multiple selection)", "녹음할 앱(복수 선택)", "要录音的应用（可多选）", "要錄音的應用程式（可多選）"),
        new("ゲームやDiscordを起動してから更新してください。選択はアプリ名で記憶されます。", "Start the game or Discord, then refresh. Selections are remembered by app name.", "게임 또는 Discord를 실행한 뒤 새로 고치세요. 선택은 앱 이름으로 저장됩니다.", "启动游戏或 Discord 后刷新。选择将按应用名称保存。", "啟動遊戲或 Discord 後重新整理。選擇將依應用程式名稱儲存。"),
        new("※ アプリ単位録音は Windows build 20348 以降で利用できます（Windows 11 推奨）。", "Per-app audio requires Windows build 20348 or later (Windows 11 recommended).", "앱별 오디오는 Windows 빌드 20348 이상이 필요합니다(Windows 11 권장).", "按应用录音需要 Windows build 20348 或更高版本（推荐 Windows 11）。", "依應用程式錄音需要 Windows build 20348 或更新版本（建議 Windows 11）。"),
        new("直前の映像を保存", "Save recent video", "최근 영상 저장", "保存最近的视频", "儲存最近的影片"),
        new("常駐中に映像を一時保持し、Alt + C で決定的な瞬間をさかのぼって保存します。", "Keeps recent video in memory and saves it with Alt + C.", "최근 영상을 임시 보관하고 Alt + C로 저장합니다.", "临时保留最近的视频，并可用 Alt + C 保存。", "暫時保留最近的影片，並可用 Alt + C 儲存。"),
        new("クリップ機能を有効にする", "Enable instant replay clips", "인스턴트 리플레이 클립 사용", "启用即时回放剪辑", "啟用即時回放剪輯"),
        new("終了時に保存するか確認する", "Ask to save the clip when exiting", "종료할 때 클립 저장 여부 확인", "退出时询问是否保存剪辑", "結束時詢問是否儲存剪輯"),
        new("クリップするモニター", "Clip display", "클립 화면", "剪辑屏幕", "剪輯螢幕"),
        new("クリップは選択した画面全体を常に録画します。", "Clips always record the entire selected display.", "클립은 선택한 화면 전체를 항상 녹화합니다.", "剪辑始终录制所选屏幕的整个画面。", "剪輯會一律錄製所選螢幕的完整畫面。"),
        new("一時データの保存先", "Temporary data location", "임시 데이터 위치", "临时数据位置", "暫存資料位置"),
        new("LumaCaptureの一時ファイルだけを使用し、通常終了時と次回起動時に削除します。", "Only LumaCapture temporary files are used here. They are removed on normal exit and the next startup.", "LumaCapture 임시 파일만 사용하며 정상 종료 및 다음 시작 시 삭제됩니다.", "此处仅使用 LumaCapture 临时文件，并会在正常退出和下次启动时删除。", "此處僅使用 LumaCapture 暫存檔案，並會在正常結束和下次啟動時刪除。"),
        new("保存する長さ", "Clip length", "클립 길이", "剪辑长度", "剪輯長度"),
        new("クリップ用の一時データは設定秒数を超えると自動削除されます。通常終了時に削除し、予期せぬ終了やPC再起動で残った場合も次回起動時に回収します。", "Temporary clip data is removed after its retention period and on normal exit. Data left by a crash or restart is recovered at the next startup.", "클립 임시 데이터는 보관 시간이 지나거나 정상 종료할 때 삭제됩니다. 충돌 또는 PC 재시작으로 남은 데이터는 다음 시작 시 정리됩니다.", "剪辑临时数据会在超过保留时间或正常退出时删除。因异常退出或重启而残留的数据会在下次启动时清理。", "剪輯暫存資料會在超過保留時間或正常結束時刪除。因意外結束或重新啟動而殘留的資料會在下次啟動時清理。"),
        new("グローバルホットキー", "Global hotkeys", "전역 단축키", "全局快捷键", "全域快速鍵"),
        new("入力欄をクリックして、使いたいキーの組み合わせを押してください。", "Click a field, then press the key combination you want to use.", "입력란을 클릭한 다음 사용할 키 조합을 누르세요.", "单击输入框，然后按下要使用的组合键。", "按一下輸入欄，然後按下要使用的組合鍵。"),
        new("録画UIを表示", "Show recording controls", "녹화 컨트롤 표시", "显示录制控件", "顯示錄製控制項"),
        new("直前クリップを保存", "Save recent clip", "최근 클립 저장", "保存最近剪辑", "儲存最近剪輯"),
        new("デフォルトに戻す", "Restore defaults", "기본값 복원", "恢复默认值", "還原預設值"),
        new("言語", "Language", "언어", "语言", "語言"),
        new("キャンセル", "Cancel", "취소", "取消", "取消"),
        new("保存", "Save", "저장", "保存", "儲存"),
        new("画質", "Quality", "화질", "画质", "畫質"),
        new("高画質  60 FPS", "High  60 FPS", "고화질  60 FPS", "高画质  60 FPS", "高畫質  60 FPS"),
        new("標準  30 FPS", "Standard  30 FPS", "표준  30 FPS", "标准  30 FPS", "標準  30 FPS"),
        new("軽量  30 FPS", "Compact  30 FPS", "경량  30 FPS", "轻量  30 FPS", "輕量  30 FPS"),
        new("録画する", "Record", "녹화", "录制", "錄製"),
        new("録画中", "Recording", "녹화 중", "正在录制", "正在錄製"),
        new("クリックで開始", "Click to start", "클릭하여 시작", "单击开始", "按一下開始"),
        new("クリックで停止", "Click to stop", "클릭하여 중지", "单击停止", "按一下停止"),
        new("録画を開始 / 停止", "Start / stop recording", "녹화 시작 / 중지", "开始 / 停止录制", "開始 / 停止錄製"),
        new("アプリ", "App", "앱", "应用", "應用程式"),
        new("設定", "Settings", "설정", "设置", "設定"),
        new("録画とクリップをすぐに。", "Record and clip instantly.", "즉시 녹화하고 클립하세요.", "立即录制和剪辑。", "立即錄製和剪輯。"),
        new("録画を開始", "Start recording", "녹화 시작", "开始录制", "開始錄製"),
        new("録画を停止", "Stop recording", "녹화 중지", "停止录制", "停止錄製"),
        new("保存フォルダーを開く", "Open save folder", "저장 폴더 열기", "打开保存文件夹", "開啟儲存資料夾"),
        new("終了", "Exit", "종료", "退出", "結束"),
        new("クリップは無効", "Clips are disabled", "클립이 비활성화됨", "剪辑已禁用", "剪輯已停用"),
        new("録画対象のアプリを選択", "Select an app to record", "녹화할 앱 선택", "选择要录制的应用", "選擇要錄製的應用程式"),
        new("表示中のアプリから録画対象を選びます。", "Choose a visible application window to record.", "표시 중인 앱 창에서 녹화 대상을 선택합니다.", "从可见的应用窗口中选择录制目标。", "從可見的應用程式視窗中選擇錄製目標。"),
        new("このアプリを録画", "Record this app", "이 앱 녹화", "录制此应用", "錄製此應用程式"),
        new("クリップを保存して終了しますか？", "Save the current clip before exiting?", "종료하기 전에 현재 클립을 저장하시겠습니까?", "退出前保存当前剪辑吗？", "結束前儲存目前的剪輯嗎？"),
        new("保存して終了", "Save and exit", "저장 후 종료", "保存并退出", "儲存並結束"),
        new("保存せず終了", "Exit without saving", "저장하지 않고 종료", "不保存并退出", "不儲存並結束"),
        new("閉じる", "Close", "닫기", "关闭", "關閉")
        ,new("選択解除", "Clear selection", "선택 해제", "清除选择", "清除選擇")
        ,new("30秒", "30 sec", "30초", "30 秒", "30 秒")
        ,new("60秒", "60 sec", "60초", "60 秒", "60 秒")
        ,new("120秒", "120 sec", "120초", "120 秒", "120 秒")
        ,new("300秒", "300 sec", "300초", "300 秒", "300 秒")
        ,new("600秒", "600 sec", "600초", "600 秒", "600 秒")
        ,new("Captura の参考ソースを開く", "Open Captura reference source", "Captura 참고 소스 열기", "打开 Captura 参考源代码", "開啟 Captura 參考原始碼")
        ,new("LumaCapture セットアップ", "LumaCapture Setup", "LumaCapture 설치", "LumaCapture 安装", "LumaCapture 安裝")
        ,new("LumaCapture をインストール", "Install LumaCapture", "LumaCapture 설치", "安装 LumaCapture", "安裝 LumaCapture")
        ,new("LumaScreenshot と同じ操作感で、画面録画・アプリ別音声・直前クリップ・容量保護をローカルで行う常駐アプリです。", "A local background app for screen recording, per-app audio, instant replay, and storage protection with the LumaScreenshot feel.", "LumaScreenshot와 같은 사용감으로 화면 녹화, 앱별 오디오, 즉시 리플레이 및 저장 공간 보호를 제공하는 로컬 앱입니다.", "一款具有 LumaScreenshot 操作体验的本地后台应用，支持屏幕录制、按应用音频、即时回放和存储保护。", "一款具有 LumaScreenshot 操作體驗的本機背景應用程式，支援螢幕錄製、依應用程式音訊、即時回放與儲存空間保護。")
        ,new("Windowsへのサインイン時に自動起動", "Start when signing in to Windows", "Windows 로그인 시 자동 시작", "登录 Windows 时自动启动", "登入 Windows 時自動啟動")
        ,new("インストール", "Install", "설치", "安装", "安裝")
        ,new("デスクトップにショートカットを作成", "Create a desktop shortcut", "바탕 화면 바로 가기 만들기", "创建桌面快捷方式", "建立桌面捷徑")
        ,new("録画と直前クリップを、いつでもすぐに。", "Record and save recent clips anytime.", "언제든 즉시 녹화하고 최근 클립을 저장하세요.", "随时录制并保存最近的剪辑。", "隨時錄製並儲存最近的剪輯。")
        ,new("メイン画面", "Main display", "주 화면", "主屏幕", "主螢幕")
        ,new("音声の確認", "Audio notice", "오디오 알림", "音频提示", "音訊提示")
        ,new("録画対象のサイズ変更", "Capture size changed", "녹화 대상 크기 변경", "录制目标大小已更改", "錄製目標大小已變更")
        ,new("アプリの選択を解除しました", "App selection cleared", "앱 선택 해제됨", "已清除应用选择", "已清除應用程式選擇")
        ,new("選択したアプリが終了したため、録画対象をメイン画面に戻しました。", "The selected app closed, so the capture target was reset to the main display.", "선택한 앱이 종료되어 녹화 대상을 주 화면으로 되돌렸습니다.", "所选应用已关闭，录制目标已恢复为主屏幕。", "所選應用程式已關閉，錄製目標已恢復為主螢幕。")
        ,new("クリップの確認", "Clip notice", "클립 알림", "剪辑提示", "剪輯提示")
        ,new("空き容量が少なくなっています", "Storage is running low", "저장 공간이 부족합니다", "存储空间不足", "儲存空間不足")
        ,new("録画を開始できません", "Recording could not start", "녹화를 시작할 수 없습니다", "无法开始录制", "無法開始錄製")
        ,new("録画を保存しています", "Saving recording", "녹화를 저장하는 중", "正在保存录制", "正在儲存錄製")
        ,new("動画と音声を仕上げています。", "Finalizing video and audio.", "동영상과 오디오를 마무리하는 중입니다.", "正在完成视频和音频处理。", "正在完成影片與音訊處理。")
        ,new("録画を保存しました", "Recording saved", "녹화가 저장되었습니다", "录制已保存", "錄製已儲存")
        ,new("録画の保存に失敗しました", "Recording could not be saved", "녹화를 저장하지 못했습니다", "无法保存录制", "無法儲存錄製")
        ,new("容量保護で録画を停止します", "Stopping to protect storage", "저장 공간 보호를 위해 녹화를 중지합니다", "为保护存储空间将停止录制", "為保護儲存空間將停止錄製")
        ,new("クリップは無効です", "Clips are disabled", "클립이 비활성화되어 있습니다", "剪辑已禁用", "剪輯已停用")
        ,new("設定の「クリップ」から有効にできます。", "Enable clips in Settings > Clips.", "설정 > 클립에서 활성화할 수 있습니다.", "可在“设置 > 剪辑”中启用。", "可在「設定 > 剪輯」中啟用。")
        ,new("通常録画中です", "A recording is in progress", "일반 녹화 중입니다", "正在进行常规录制", "正在進行一般錄製")
        ,new("直前クリップは録画停止後に自動で再開します。", "Instant replay resumes after the recording stops.", "녹화가 중지되면 즉시 리플레이가 다시 시작됩니다.", "录制停止后即时回放会自动恢复。", "錄製停止後即時回放會自動恢復。")
        ,new("クリップを保存しています", "Saving clip", "클립 저장 중", "正在保存剪辑", "正在儲存剪輯")
        ,new("クリップを保存しました", "Clip saved", "클립이 저장되었습니다", "剪辑已保存", "剪輯已儲存")
        ,new("クリップを保存できません", "Clip could not be saved", "클립을 저장하지 못했습니다", "无法保存剪辑", "無法儲存剪輯")
        ,new("クリップ機能を開始できません", "Instant replay could not start", "즉시 리플레이를 시작할 수 없습니다", "无法启动即时回放", "無法啟動即時回放")
        ,new("設定を保存しました", "Settings saved", "설정이 저장되었습니다", "设置已保存", "設定已儲存")
        ,new("次の録画から新しい設定を使います。", "The new settings will be used for the next recording.", "다음 녹화부터 새 설정이 적용됩니다.", "新设置将用于下一次录制。", "新設定將用於下一次錄製。")
        ,new("ホットキーを登録できません", "Hotkeys could not be registered", "단축키를 등록할 수 없습니다", "无法注册快捷键", "無法註冊快速鍵")
        ,new("別のアプリと重複している可能性があります。設定から変更してください。", "They may conflict with another app. Change them in Settings.", "다른 앱과 충돌할 수 있습니다. 설정에서 변경하세요.", "可能与其他应用冲突，请在设置中更改。", "可能與其他應用程式衝突，請在設定中變更。")
        ,new("保存先を開けません", "Save folder could not be opened", "저장 폴더를 열 수 없습니다", "无法打开保存文件夹", "無法開啟儲存資料夾")
        ,new("マイクを録音しない（デフォルト）", "Do not record a microphone (default)", "마이크 녹음 안 함(기본)", "不录制麦克风（默认）", "不錄製麥克風（預設）")
        ,new("収録する音の反応", "Recording audio level", "녹음할 소리 반응", "录制音频电平", "錄製音訊電平")
        ,new("マイクの反応", "Microphone level", "마이크 반응", "麦克风电平", "麥克風電平")
        ,new("待機中", "Waiting", "대기 중", "等待中", "等待中")
        ,new("反応あり", "Active", "입력 감지", "有声音", "有聲音")
        ,new("設定画面を開いている間だけ軽量メーターで確認します。", "Lightweight meters run only while Settings is open.", "설정 화면이 열려 있는 동안에만 가벼운 미터로 확인합니다.", "轻量电平表仅在设置窗口打开时运行。", "輕量電平表僅在設定視窗開啟時執行。")
        ,new("録画対象のアプリが終了しました", "The recorded app has closed", "녹화 대상 앱이 종료되었습니다", "录制目标应用已关闭", "錄製目標應用程式已關閉")
        ,new("録画対象のアプリが終了したため、録画を自動停止します。", "Recording will stop automatically because the recorded app has closed.", "녹화 대상 앱이 종료되어 녹화를 자동으로 중지합니다.", "录制目标应用已关闭，将自动停止录制。", "錄製目標應用程式已關閉，將自動停止錄製。")
        ,new("選択できるアプリが見つかりません。", "No selectable applications were found.", "선택할 수 있는 앱이 없습니다.", "未找到可选择的应用。", "找不到可選擇的應用程式。")
        ,new("録画するアプリを選択してください。", "Select an application to record.", "녹화할 앱을 선택하세요.", "请选择要录制的应用。", "請選擇要錄製的應用程式。")
        ,new("録画対象のアプリが画面上に見つかりません。", "The target application is not visible on a display.", "대상 앱이 화면에 표시되지 않습니다.", "目标应用未显示在屏幕上。", "目標應用程式未顯示在螢幕上。")
        ,new("録画対象のアプリが起動していないか、最小化されています。", "The target application is not running or is minimized.", "대상 앱이 실행 중이 아니거나 최소화되어 있습니다.", "目标应用未运行或已最小化。", "目標應用程式未執行或已最小化。")
        ,new("LumaCapture アンインストール", "Uninstall LumaCapture", "LumaCapture 제거", "卸载 LumaCapture", "解除安裝 LumaCapture")
        ,new("LumaCapture を削除しますか？", "Uninstall LumaCapture?", "LumaCapture를 제거하시겠습니까?", "要卸载 LumaCapture 吗？", "要解除安裝 LumaCapture 嗎？")
        ,new("アプリ本体を削除します。保存済みの動画と個人設定は残ります。", "The app will be removed. Saved videos and personal settings will remain.", "앱은 제거되지만 저장된 동영상과 개인 설정은 유지됩니다.", "应用将被删除，已保存的视频和个人设置将保留。", "應用程式將被移除，已儲存的影片與個人設定將保留。")
        ,new("削除", "Uninstall", "제거", "卸载", "解除安裝")
        ,new("LumaCapture を削除しています", "Uninstalling LumaCapture", "LumaCapture 제거 중", "正在卸载 LumaCapture", "正在解除安裝 LumaCapture")
        ,new("しばらくお待ちください。", "Please wait.", "잠시 기다려 주세요.", "请稍候。", "請稍候。")
        ,new("インストールを準備しています…", "Preparing installation…", "설치 준비 중…", "正在准备安装…", "正在準備安裝…")
        ,new("インストールが完了しました", "Installation complete", "설치 완료", "安装完成", "安裝完成")
        ,new("LumaCapture はシステムトレイに常駐します。", "LumaCapture will run in the system tray.", "LumaCapture는 시스템 트레이에서 실행됩니다.", "LumaCapture 将在系统托盘中运行。", "LumaCapture 將在系統匣中執行。")
        ,new("起動", "Launch", "실행", "启动", "啟動")
        ,new("インストールできませんでした", "Installation failed", "설치 실패", "安装失败", "安裝失敗")
        ,new("再試行", "Retry", "다시 시도", "重试", "重試")
        ,new("アンインストールが完了しました", "Uninstallation complete", "제거 완료", "卸载完成", "解除安裝完成")
        ,new("保存済みの動画と個人設定は残っています。", "Saved videos and personal settings remain.", "저장된 동영상과 개인 설정은 유지됩니다.", "已保存的视频和个人设置仍会保留。", "已儲存的影片與個人設定仍會保留。")
        ,new("ご利用ありがとうございました。", "Thank you for using LumaCapture.", "LumaCapture를 이용해 주셔서 감사합니다.", "感谢使用 LumaCapture。", "感謝使用 LumaCapture。")
        ,new("削除を完了できませんでした", "Uninstallation could not be completed", "제거를 완료하지 못했습니다", "无法完成卸载", "無法完成解除安裝")
    };

    public static AppLanguage CurrentLanguage { get; private set; } = AppLanguage.Japanese;

    public static void SetLanguage(AppLanguage language) => CurrentLanguage = language;

    public static AppLanguage DetectSystemLanguage()
    {
        var name = CultureInfo.CurrentUICulture.Name;
        if (name.StartsWith("ja", StringComparison.OrdinalIgnoreCase)) return AppLanguage.Japanese;
        if (name.StartsWith("ko", StringComparison.OrdinalIgnoreCase)) return AppLanguage.Korean;
        if (name.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase) || name.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase) || name.StartsWith("zh-HK", StringComparison.OrdinalIgnoreCase)) return AppLanguage.ChineseTraditional;
        if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return AppLanguage.ChineseSimplified;
        return AppLanguage.English;
    }

    public static string T(string text)
    {
        var phrase = Phrases.FirstOrDefault(item => string.Equals(item.Japanese, text, StringComparison.Ordinal)
            || string.Equals(item.English, text, StringComparison.Ordinal)
            || string.Equals(item.Korean, text, StringComparison.Ordinal)
            || string.Equals(item.ChineseSimplified, text, StringComparison.Ordinal)
            || string.Equals(item.ChineseTraditional, text, StringComparison.Ordinal));
        if (phrase is null) return text;
        return CurrentLanguage switch
        {
            AppLanguage.English => phrase.English,
            AppLanguage.Korean => phrase.Korean,
            AppLanguage.ChineseSimplified => phrase.ChineseSimplified,
            AppLanguage.ChineseTraditional => phrase.ChineseTraditional,
            _ => phrase.Japanese
        };
    }

    public static string Seconds(int seconds) => CurrentLanguage switch
    {
        AppLanguage.English => $"{seconds} sec",
        AppLanguage.Korean => $"{seconds}초",
        AppLanguage.ChineseSimplified => $"{seconds} 秒",
        AppLanguage.ChineseTraditional => $"{seconds} 秒",
        _ => $"{seconds} 秒"
    };

    public static string ClipFinalizing(int seconds) => CurrentLanguage switch
    {
        AppLanguage.English => $"Finalizing the available video from the last {seconds} seconds.",
        AppLanguage.Korean => $"최근 {seconds}초 동안의 사용 가능한 영상을 처리하고 있습니다.",
        AppLanguage.ChineseSimplified => $"正在处理最近 {seconds} 秒内可用的视频。",
        AppLanguage.ChineseTraditional => $"正在處理最近 {seconds} 秒內可用的影片。",
        _ => $"直前 {seconds} 秒のうち保存できる映像を仕上げています。"
    };

    public static string StorageRemaining(double gigabytes) => CurrentLanguage switch
    {
        AppLanguage.English => $"The save location has {gigabytes:F1} GB free.",
        AppLanguage.Korean => $"저장 위치의 여유 공간은 {gigabytes:F1}GB입니다.",
        AppLanguage.ChineseSimplified => $"保存位置剩余 {gigabytes:F1} GB。",
        AppLanguage.ChineseTraditional => $"儲存位置剩餘 {gigabytes:F1} GB。",
        _ => $"保存先の空き容量は {gigabytes:F1} GB です。"
    };

    public static string AutoStopStorage(double gigabytes) => CurrentLanguage switch
    {
        AppLanguage.English => $"Only {gigabytes:F1} GB remains. The recording will be saved safely.",
        AppLanguage.Korean => $"여유 공간이 {gigabytes:F1}GB이므로 녹화를 안전하게 저장합니다.",
        AppLanguage.ChineseSimplified => $"仅剩 {gigabytes:F1} GB，将安全保存录制。",
        AppLanguage.ChineseTraditional => $"僅剩 {gigabytes:F1} GB，將安全儲存錄製。",
        _ => $"空き容量が {gigabytes:F1} GB になったため、安全に保存します。"
    };

    public static string ApplicationWindowSizeChanged(int oldWidth, int oldHeight, int newWidth, int newHeight) => CurrentLanguage switch
    {
        AppLanguage.English => $"The selected app changed from {oldWidth} × {oldHeight} to {newWidth} × {newHeight}. Recording is continuing.",
        AppLanguage.Korean => $"선택한 앱의 크기가 {oldWidth} × {oldHeight}에서 {newWidth} × {newHeight}(으)로 변경되었습니다. 녹화는 계속됩니다.",
        AppLanguage.ChineseSimplified => $"所选应用的大小已从 {oldWidth} × {oldHeight} 更改为 {newWidth} × {newHeight}。录制仍在继续。",
        AppLanguage.ChineseTraditional => $"所選應用程式的大小已從 {oldWidth} × {oldHeight} 變更為 {newWidth} × {newHeight}。錄製仍在繼續。",
        _ => $"選択したアプリのサイズが {oldWidth} × {oldHeight} から {newWidth} × {newHeight} に変わりました。録画は継続中です。"
    };

    public static void Apply(DependencyObject root)
    {
        if (root is Window window && !string.IsNullOrWhiteSpace(window.Title)) window.Title = T(window.Title);
        if (root is TextBlock textBlock && !string.IsNullOrWhiteSpace(textBlock.Text)) textBlock.Text = T(textBlock.Text);
        if (root is HeaderedContentControl headered && headered.Header is string header) headered.Header = T(header);
        if (root is ContentControl contentControl && contentControl.Content is string content) contentControl.Content = T(content);
        if (root is FrameworkElement element && element.ToolTip is string tooltip) element.ToolTip = T(tooltip);
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) Apply(child);
    }
}
