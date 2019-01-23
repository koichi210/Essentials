// プロジェクトで使用する定数定義

#include<stdio.h>
#include<stdlib.h>
#include<string.h>

// language
#define ENGLISH			(0)

//デバッグ定義
#define TOOLBAR_USE		(1)
#define ALLSTEAL_USE	(0)

// 基本定数
#define STR_BUFF			(256)	// 文字列最大バッファ
#define START_STONE			(4)		// スタート時の石の数
#define WIN_MIN_SIZE		(250)	// ウィンドウ表示のの最小値
#define TEXT_OFFSET			(100)	// 時間を表示しているテキストの横幅
#define FRAME_OFFSET		(25)	// 画面端から枠までの距離
#define NUM_OFFSET			(5)		// 画面端から枠番号までの距離
#define FRAME2TEXT_OFFSET	(20)	// 基盤枠とテキスト枠との距離
#define TEXT2TEXT_OFFSET	(40)	// 黒と白の距離
#define SCORE_OFFSET		(60)	// スコア表示の距離
#define TIME2TEXT_OFFSET	(20)	// 色名と時間との距離
#define DEFAULT_TIME_POS_X	(181)	// 時間表示（X座標）のDEFAULT値
#define DEFAULT_TIME_POS_Y	(60)	// 時間表示（Y座標）のDEFAULT値
#define TIME_OUT			(1)		// タイムアウト

// 識別子
#define EMY					(1)		// 自分
#define ENEMY				(2)		// 敵

// タイマID
#define COM_TIMER_ID		(1)		// COM の動くタイマID
#define BLACK_TIMER_ID		(2)		// 黒 の動くタイマID
#define WHITE_TIMER_ID		(3)		// 白 の動くタイマID

// タイマ
#define VS_MODE_INTERVAL	(10)	// PLAYER VS COM 時のインターバル(10msくらいかな？)
#define COM_MODE_INTERVAL	(10)	// COM VS COM 時のインターバル
#define COUNT_DOWN_CYC		(100)	// カウントダウン周期

// 色
#define UNKNOWN				(0)		// 不定
#define BLACK				(1)		// 黒
#define WHITE				(2)		// 白
#define BLACK_NAME			"黒"	// 黒の名前
#define WHITE_NAME			"白"	// 白の名前

// マス
#define LINE_MIN		(1)						// １辺のマスの最小値
#define LINE_MAX		(8)						// １辺のマスの最大値
#define	MAS_MAX			(LINE_MAX * LINE_MAX)	// マスの最大値
#define TURN_MAX		(MAS_MAX-START_STONE)	// ターンの最大数

// 方向
#define ORIENT_MAX		(8)	// 方向最大数
#define LEFT			(1)	// 進む方向（左）
#define RIGHT			(2)	// 進む方向（右）
#define UP				(3)	// 進む方向（上）
#define DOWN			(4)	// 進む方向（下）
#define LEFT_UP			(5)	// 進む方向（左上）
#define LEFT_DOWN		(6)	// 進む方向（左下）
#define RIGHT_UP		(7)	// 進む方向（右上）
#define RIGHT_DOWN		(8)	// 進む方向（右下）

// 棋譜の送り戻し
#define VERS			(1)	// 一手送る
#define REVERS			(2)	// 一手戻す

// ターン
#define	PLAYER			(1)	// PLAYER
#define	COM				(2)	// COMPUTER

// VS
#define	PLAYER_PLAYER	(1)	// 人 VS 人
#define	PLAYER_COM		(2)	// 人 VS COM
#define	COM_PLAYER		(3)	// COM VS 人
#define	COM_COM			(4)	// COM VS COM

// ゲームステータス
#define GAME_INIT		(1)	// ゲーム開始前
#define GAME_PLAY		(2)	// ゲーム中
#define GAME_STOP		(3)	// ゲーム停止
#define GAME_END		(4)	// ゲーム終了

// manager
#define	PUT_ERROR		(-1)	// PutCheck ERROR
#define	PUT_NORMAL		(0)		// PutCheck NORMAL
#define	NORMAL_RETURN	(0)		// 正常リターン
#define ERROR_RETURN	(-1)	// 異常リターン
#define CHK_ON			(1)		// チェックのみ（PutCheck)
#define PUT_ON			(2)		// 置ける場所をサーチ（PutCheck)

// 棋譜
#define KIHU_DIR		"オセロ棋譜\\"
#define KIHU_NAME		"DATE"
#define SHOW			(1)	// 棋譜を見る
#define SAVE			(2)	// 棋譜を保存

// COM
#define	STARTER			(1)	// 序盤(COM)
#define	CENTER			(2)	// 中盤(COM)
#define	FINISHER		(3)	// 終盤(COM)

// 設定値の状態
#define ON				(1)
#define OFF				(0)

// メニューバー設定値
#define	ENABLE			(1)
#define	CHECK			(2)

// COM用 返り値
#define COM_ERROR		(-1)	// COM異常終了
#define COM_NORMAL		(0)		// COM正常終了
#define STEAL			(3)		// 取られる
#define NOT_STEAL		(4)		// 取られない
#define STAR			(5)		// 置くところは星
#define	NOT_STAR		(6)		// 星ではない
#define NSTAR			(7)		// 置くところは星の隣
#define	NOT_NSTAR		(8)		// 星の近くではない

#define STR_WEIGHT		(15)	// 文字幅
#define STR_HEIGHT		(15)	// 文字高さ

// 各パラメータの初期値
#define DEFAULT_PLAY_MODE	PLAYER_COM
#define DEFAULT_TIME		(-1)	// 持ち時間（秒）
#define DEFAULT_COM_LEVEL	(3)
#define DEFAULT_PUT_NOTICE	ON
