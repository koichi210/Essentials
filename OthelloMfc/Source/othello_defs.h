// othello_defs.h : 盤面・ゲーム進行で共通に使う定数と型

#pragma once

// 盤面
constexpr int kBoardSize		= 8;							// 1辺のマス数
constexpr int kInitialStones	= 4;							// 開始時に置かれている石の数
constexpr int kMaxMoves			= kBoardSize * kBoardSize - kInitialStones;	// 1局の最大手数
constexpr int kDirectionCount	= 8;							// 石を挟める方向の数

// 方向ごとの裏返る石の数（並びは Board::kDirections と対応）
using FlipCounts = std::array<int, kDirectionCount>;

// 石の色（マスの状態）
enum class Stone : char
{
	kNone	= 0,
	kBlack	= 1,
	kWhite	= 2,
};

// 相手の色
inline Stone Opponent(Stone color)
{
	return (color == Stone::kBlack) ? Stone::kWhite : Stone::kBlack;
}

// 色の表示名（棋譜ファイルにもこの名前で書き出す）
inline LPCTSTR ColorName(Stone color)
{
	return (color == Stone::kBlack) ? _T("黒") : _T("白");
}

// 対戦モード
enum class PlayMode
{
	kPlayerVsPlayer,
	kPlayerVsCom,	// 人が黒
	kComVsPlayer,	// COMが黒
	kComVsCom,
};

// ゲームの状態
enum class GameState
{
	kInit,	// 開始前
	kPlay,	// 対局中
	kStop,	// 一時停止
	kEnd,	// 終局
};
