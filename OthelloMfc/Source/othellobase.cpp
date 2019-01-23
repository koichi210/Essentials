#include "stdafx.h"
#include "othelloDef.h"
#include "othellobase.h"

// construct
COthelloBase::COthelloBase()
{
	// none
}

// destruct
COthelloBase::~COthelloBase()
{
	// none
}

// +-+-+-+- initialize +-+-+-+-+-
void COthelloBase::BaseInit()
{

	::memset(Table, UNKNOWN, sizeof(Table)) ;
	Table[3][3] = WHITE ;
	Table[3][4] = BLACK ;
	Table[4][3] = BLACK ;
	Table[4][4] = WHITE ;

}

// +-+-+-+- copy of Table +-+-+-+-+-
//int	m_Table[LINE_MAX][LINE_MAX]	éÊìæêÊTable(OUT)
void COthelloBase::GetTable(char m_Table[LINE_MAX][LINE_MAX])
{
	::memcpy(m_Table, Table, sizeof(Table)) ;
}

// +-+-+-+- put stone +-+-+-+-+-
// CPint	start					base matrix of put stone(IN)
// int		put_stone[ORIENT_MAX]	orientation and number of put stone(IN)
// int		color					color of put stone(IN)
void COthelloBase::PutStone(CPoint start, int put_stone[ORIENT_MAX], int color, int attrib)
{
	int count = 0 ;		// put number
	int orient = 0 ;	// put orientation
	int	X = start.x-1 ;	// put matrix(X)
	int Y = start.y-1 ;	// put matrix(Y)

	// put now matrix
	if(attrib == REVERS){
		Table[Y][X] = UNKNOWN ;
	}else{
		Table[Y][X] = color ;
	}

	for(orient=0; orient<ORIENT_MAX; orient++){
		X = start.x-1 ;
		Y = start.y-1 ;
		for(count=1; count<=put_stone[orient];count++){
			switch(orient+1){
			case LEFT :
				X -= 1 ;
				break ;

			case RIGHT :
				X += 1 ;
				break ;

			case UP :
				Y -= 1 ;
				break ;

			case DOWN :
				Y += 1 ;
				break ;

			case LEFT_UP :
				X -= 1 ;
				Y -= 1 ;
				break ;

			case LEFT_DOWN :
				X -= 1 ;
				Y += 1 ;
				break ;

			case RIGHT_UP :
				X += 1 ;
				Y -= 1 ;
				break ;

			case RIGHT_DOWN :
				X += 1 ;
				Y += 1 ;
				break ;

			default :
				break ;
			}
			Table[Y][X] = color ;
		}
	}
}

// +-+-+- calculation +-+-+-
// int	* black_num		number of black stone(OUT)
// int	* white_num		number of white stone(OUT)
void COthelloBase::CntStone(int * black_num, int * white_num)
{
	int x,y ;	// loop counter

	// init
	*black_num = 0 ;
	*white_num = 0 ;

	for(y=0; y<LINE_MAX; y++){
		for(x=0; x<LINE_MAX; x++){
			if(Table[y][x] == BLACK){
				// *black_num ++ ÇÕNGÅBCåæåÍÇÃÇ®çÏñ@Ç≈Ç∑ÅB
				*black_num += 1 ;
			}else if(Table[y][x] == WHITE){
				*white_num += 1 ;
			}
		}
	}
}

// +-+-+- change color +-+-+-
// return == BLACK		black turn
// return == WHITE		white turn
// return == GAME_END	game set
// int	mycolor			my color(IN)
int COthelloBase::PutCheckWhich(int mycolor)
{
	int enemy_clr ;		// enemy color
	int rt ;			// return value

	// get enemy color
	if(mycolor == BLACK){
		enemy_clr = WHITE ;
	}else{
		enemy_clr = BLACK ;
	}

	// validate check to enemy put matrix
	rt = PutCheckAny(enemy_clr) ;
	if(rt != PUT_NORMAL){
		// enemy put matrix none
		rt = PutCheckAny(mycolor) ;
		if(rt != PUT_NORMAL){
			// and me too so game set
			rt = GAME_END ;

		}else{
			// i m put(put continue)
			rt = mycolor ;
		}
	}else{
		// enemy is put
		rt = enemy_clr ;
	}

	return rt ;
}

// +-+-+- check matrix to put stone +-+-+-
// return == PUT_NORMAL	the stone is put
// return != PUT_NORMAL	the stone is not put
// int	color			color of check(IN)
int COthelloBase::PutCheckAny(int color)
{
	CPoint z ;		// matrix wannna put
	int rt = 0 ;	// return value

	for(z.y=1; z.y<=LINE_MAX;z.y++){
		for(z.x=1; z.x<=LINE_MAX;z.x++){
			rt = PutCheck(z, color, CHK_ON, NULL) ;
			if(rt == PUT_NORMAL){
				// put matrix found
				return rt ;
			}
		}
	}
	// put matrix not found
	return PUT_ERROR ;
}

// +-+-+- catch judge select matrix +-+-+-
// return == PUT_NORMAL				the stone is put matrix
// return == PUT_ERROR				the stone is not put matrix
// CPoint	in						wannna matrix(IN)
// int		color					my color(IN)
// int		attrib					CHK_ON = put judge(IN)
//									PUT_ON = put(IN)
// int		put_stone[ORIENT_MAX]	orientation and number in put stone(OUT)
int COthelloBase::PutCheck(CPoint in, int color, int attrib, int put_stone[ORIENT_MAX])
{
	CPoint z ;		// check matrix
	int put_flg		= PUT_ERROR ;	// no put flag
	int enemy_color	= 0 ;	// enemy color
	int line		= 0 ;	// loop counter
	int orient		= 0 ;	// orientation
	int stone_cnt	= 0 ;	// overturn stone number

	// get enemy color
	if(color == BLACK){
		enemy_color = WHITE ;
	}else{
		enemy_color = BLACK ;
	}

	if(	Table[in.y-1][in.x-1] != 0){
		// select matrix is already put
		return put_flg ;
	}

	for(orient=1;orient<=ORIENT_MAX;orient++){
		::memcpy(&z, &in, sizeof(CPoint)) ;
		stone_cnt = 0 ;
		for(line=1;line<=LINE_MAX;line++){
			switch(orient){
			case LEFT :
				z.x-- ;
				break ;

			case RIGHT :
				z.x++ ;
				break ;

			case UP :
				z.y-- ;
				break ;

			case DOWN :
				z.y++ ;
				break ;

			case LEFT_UP :
				z.x-- ;
				z.y-- ;
				break ;

			case LEFT_DOWN :
				z.x-- ;
				z.y++ ;
				break ;

			case RIGHT_UP :
				z.x++ ;
				z.y-- ;
				break ;

			case RIGHT_DOWN :
				z.x++ ;
				z.y++ ;
				break ;

			default :
				// maby not in...
				break ;
			}

			if(z.x < LINE_MIN || z.x > LINE_MAX ||
				z.y < LINE_MIN || z.y > LINE_MAX){
				// over the table
				break ;
			}

			if(Table[z.y-1][z.x-1] == 0){
				// no stone this point over
				break ;
			}else if(Table[z.y-1][z.x-1] == enemy_color){
				// found enemy color
				stone_cnt++ ;
			}else if(Table[z.y-1][z.x-1] == color){
				// found my color
				if(stone_cnt > 0){
					// catch enemy color
					if(attrib == CHK_ON){
						return PUT_NORMAL ;
					}else{
						put_flg = PUT_NORMAL ;
						put_stone[orient-1] = stone_cnt ;
					}
				}
				// first found is mine that not catch enemy color
				// if enemy color over there my color then it after not catch enemy color
				break ;
			}
		}
	}

	return put_flg ;
}

// +-+-+- notice put matrix +-+-+-
// int		clr		check color
// CPoint	zahyo	put matrix
void COthelloBase::GetPutNotice(int clr, CPoint zahyo[MAS_MAX])
{
	CPoint o_z[LINE_MAX*LINE_MAX] ;
	CPoint z ;
	int rt ;
	int cnt = 0 ;

	::memset(zahyo, 0, sizeof(zahyo)) ;

	for(z.x=1;z.x<=LINE_MAX;z.x++){
		for(z.y=1;z.y<=LINE_MAX;z.y++){
			rt = PutCheck(z, clr, CHK_ON, NULL) ;
			if(rt == PUT_NORMAL){
				::memcpy(&zahyo[cnt++], &z, sizeof(z)) ;
			}
		}
	}

}
