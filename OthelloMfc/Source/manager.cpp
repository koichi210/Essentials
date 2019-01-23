#include "stdafx.h"
#include "othelloDef.h"
#include "othellobase.h"
#include "manager.h"

// construct
CManager::CManager()
{
	play_mode = DEFAULT_PLAY_MODE ;
	com_level = DEFAULT_COM_LEVEL ;
	com_interval = VS_MODE_INTERVAL ;
	put_notice = DEFAULT_PUT_NOTICE ;

	GameValueInit() ;
}

// destruct
CManager::~CManager()
{
	// none
}

// +-+-+-+- initialize +-+-+-+-+-
void CManager::GameValueInit()
{
	CPoint zahyo ;

	::memset(&zahyo, 0, sizeof(zahyo)) ;
	::memset(KihuTable, 0, sizeof(KihuTable)) ;
	color = BLACK ;
	turn_cnt = 0 ;
	turning_point = 0;

	BaseInit() ;
	UpDateGameState(GAME_INIT) ;
}

// +-+-+-+- read KIHU from file +-+-+-+-+-
int CManager::GetKihuFile(CString path, int *color)
{
	FILE *fp ;
	CPoint put_map ;
	int put_rt ;
	int put_stone[ORIENT_MAX] ;
	int	rt = NORMAL_RETURN ;
	char str[STR_BUFF] ;
	char black_name[STR_BUFF] ;
	int number ;
	char x_str ;
	char y_str ;
	char clr[STR_BUFF] ;
	int put_clr ;

	::memset(&str, 0, sizeof(str)) ;
	::memset(&black_name, 0, sizeof(black_name)) ;

	// open the read file
	fp = fopen(path, "r") ;
	if(fp != NULL){
		GetColorName(BLACK, black_name) ;

		// read data to end of file
		while(fgets(str, STR_BUFF, fp) != NULL){
			sscanf(str, "%d : %c.%c %s",&number,&x_str,&y_str,clr) ;
			// validate judge to value
			if(1 <= number && number <= TURN_MAX){
				put_map.x = x_str - 0x40 ;
				put_map.y = atoi(&y_str) ;
				if((put_map.x >= LINE_MIN && put_map.x <= LINE_MAX) &&
					(put_map.y >= LINE_MIN && put_map.y <= LINE_MAX) ){
					// validate value
					if(strcmp(clr, black_name) == 0){
						put_clr = BLACK ;
					}else{
						put_clr = WHITE ;
					}
					::memset(put_stone, 0, sizeof(put_stone)) ;
					put_rt = PutCheck(put_map, put_clr, PUT_ON, put_stone) ;
					if(put_rt != PUT_NORMAL){
						rt = ERROR_RETURN ;
						break ;
					}else{
						PutStone(put_map, put_stone, put_clr) ;
						UpDateKihu(put_map, put_stone) ;
					}
				}
			}
		}

		fclose(fp) ;
	}

	if(rt != ERROR_RETURN){
		*color = put_clr ;
	}

	return rt ;
}

// +-+-+-+- save KIHU to DB +-+-+-+-+-
// CPoint	put_map		matrix of put stone(IN)
void CManager::UpDateKihu(CPoint put_map, int put_stone[ORIENT_MAX])
{
	// memory table íœŒó•â
	::memcpy(KihuTable[turn_cnt].Table, Table, sizeof(Table)) ;

	// memory matrinx
	:: memcpy(&KihuTable[turn_cnt].zahyo, &put_map, sizeof(put_map)) ;

	// memory color
	KihuTable[turn_cnt].color = color ;

	// memory orient Ì—pŒó•â
	if(put_stone){
		::memcpy(KihuTable[turn_cnt].put_stone, put_stone, sizeof(put_stone) * ORIENT_MAX) ;
	}

	// incliment to point
	turn_cnt++ ;

	// reset to move turn on modern
	turning_point = turn_cnt ;
}

// +-+-+-+- get KIHU(file / dialog) +-+-+-+-+-
// CString path		attribute element(IN)
void CManager::GetKihu(int attrib, CString path)
{
	FILE *fp ;							// file pointer
	char kihu_wk[STR_BUFF*MAS_MAX] ;	// work table for KIHU
	char temp[STR_BUFF] ;				// work table for KIHU
	char color[STR_BUFF] ;				// color string
	int i ;								// loop counter

	// init
	memset(&kihu_wk, 0, sizeof(kihu_wk)) ;
	memset(&color, 0, sizeof(color)) ;

	// make data
#if ENGLISH
		sprintf(kihu_wk, "***** KIHU ***** \n") ;
#else
		sprintf(kihu_wk, "***** Šû•ˆ ***** \n") ;
#endif
	for(i=1; i<TURN_MAX+1;i++){
		if(KihuTable[i].color == 0){
			break ; 
		}

		GetColorName(KihuTable[i].color, color) ;
		sprintf(temp, "%2d : %c.%d %s\n",
			i,
			0x40 + KihuTable[i].zahyo.x,
			KihuTable[i].zahyo.y,
			color) ;
		strcat(kihu_wk,temp) ;
	}

	if(attrib == SAVE){	// save KIHU
		fp = fopen(path, "w") ;
		if(fp != NULL){
			// if opened file then output KIHU
			fprintf(fp, "%s",kihu_wk) ;
			fclose(fp) ;
		}
	}else if(attrib == SHOW){ // view KIHU
		AfxMessageBox(kihu_wk) ;
	}
}

// +-+-+- judge turn +-+-+-
// return == PLAYER		player turn
// return == COM		COM turn
// int	play_mode		PLYAER || COM VS mode(IN)
// int	color			modern color(IN)
int CManager::GetTurnNow(int play_mode, int color)
{
	int rt = 0 ;	// return value

	if( (play_mode == PLAYER_COM &&
		color == WHITE) ||
		(play_mode == COM_PLAYER &&
		color == BLACK) ||
		(play_mode == COM_COM) ){
		rt = COM ;
	}else{
		rt = PLAYER ;
	}

	return rt ;
}

// +-+-+- update game state +-+-+-
// int	gsts	game state(IN)
void CManager::UpDateGameState(int gsts)
{
	// if change game state then wanna anything proc as here
	// ex) Disable in menu etc...
	game_state = gsts ;
}

// +-+-+- get color name +-+-+-
// int	clr		color(IN)
// char	name[STR_BUFF](OUT)
void CManager::GetColorName(int clr, char name[STR_BUFF])
{
	if(clr == BLACK){
		strcpy(name, BLACK_NAME) ;
	}else{
		strcpy(name, WHITE_NAME) ;
	}
}


void CManager::GetEnemyColor(int my_clr, int * enmy_clr)
{
	if(my_clr == BLACK)
		*enmy_clr = WHITE ;
	else
		*enmy_clr = BLACK ;
}
