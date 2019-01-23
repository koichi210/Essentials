#include "stdafx.h"
#include "othelloDef.h"
#include "othellobase.h"
#include "com.h"

// コンストラクタ
CComProc::CComProc()
{
	// before radom proc
	srand( (unsigned)time( NULL ) );
}

// デストラクタ
CComProc::~CComProc()
{
	// none
}

// +-+-+-+- COM think main routin +-+-+-+-+-
// return == COM_NORMAL	the stone is put
// return != COM_NORMAL	the stone is not put
// CPoint	* z			COM put matrix(OUT)
// int		myclr		COM color(IN)
// int		com_level	COM level(IN)
// int		turn_cnt	total turn number of modern(IN)
int CComProc::Turn(CPoint * z, int myclr, int com_level, int tn_cnt)
{
	int rt = COM_ERROR ;	// return value

	// set ComClass->turn_cnt
	turn_cnt = tn_cnt ;

	if((MAS_MAX-START_STONE) < turn_cnt || turn_cnt < 0){
		return COM_ERROR ;
	}else if(turn_cnt < (MAS_MAX / 3) ){
		field = STARTER ;
	}else if(turn_cnt < (MAS_MAX / 3 * 2) ){
		field = CENTER ;
	}else{
		field = FINISHER ;
	}

	// save original table
	::memcpy(OrgTable, Table, sizeof(Table)) ;

	// set manager::turn in COM color
	color = myclr ;

	switch(com_level){
	case 1 :
		rt = ComLevel1(z) ;
		break ;

	case 2 :
		rt = ComLevel2(z) ;
		break ;

	case 3 :
		rt = ComLevel3(z) ;
		break ;
	case 4:
	default :
		// for judge miss
		// move left up
		rt = LeftUp(z) ;
		break ;
	}

	if(z->x < LINE_MIN || z->x > LINE_MAX ||
		z->y < LINE_MIN || z->y > LINE_MAX){
		// put matrix is outsize of table
		rt = COM_ERROR ;
	}

	return rt ;
}

// +-+-+-+- レベル1 +-+-+-+-
int CComProc::ComLevel1(CPoint * z)
{
	CPoint put[MAS_MAX] ;	// put matrix
	int put_num = 0 ;		// put number
	int rt = COM_ERROR ;	// return balue

	// init
	::memset(put, 0, sizeof(put)) ;

	// center is most high priority
	rt = Center(put, &put_num) ;

	if(rt == COM_NORMAL){
		rt = DetermineLocation(put, z, put_num) ;
	}

	return rt ;
}

// +-+-+-+- レベル2 +-+-+-+-
int CComProc::ComLevel2(CPoint * z)
{
	CPoint put[MAS_MAX] ;	// put matrix
	int put_num = 0 ;		// put number
	int rt = COM_ERROR ;	// return vaule

	// init
	::memset(put, 0, sizeof(put)) ;

	// change prioriy in situation
	if(field == STARTER){
		// the early stage is center
		rt = Center(put, &put_num) ;
	}else if(field == CENTER){
		// the middle stage is little overturn
		rt = GetMin(put, &put_num) ;
	}else{
		// the last stage is a lot of overturn
		rt = GetMax(put, &put_num) ;
	}

	if(rt == COM_NORMAL){
		rt = DetermineLocation(put, z, put_num) ;
	}

	return rt ;
}

// +-+-+-+- レベル3 +-+-+-+-
int CComProc::ComLevel3(CPoint * z)
{
	CPoint put[MAS_MAX] ;	// put matrix
	int put_num = 0 ;		// put number
	int rt = COM_ERROR ;	// return value

	// init
	::memset(put, 0, sizeof(put)) ;

	// put stone matrix guid from open theory(sort 2 priorty)
	rt = GetKaiho(put, &put_num) ;

	if(rt == COM_NORMAL){
		rt = DetermineLocation(put, z, put_num) ;
	}

	return rt ;
}

// +-+-+-+- Determine put COM stone location +-+-+-+-+-
// return == COM_NORMAL	the stone is put
// return != COM_NORMAL	the stone is not put
int CComProc::DetermineLocation(CPoint put[MAS_MAX], CPoint * determ_put, int put_num)
{
	CPoint corner ;			// corner matrix
	CPoint nstar ;			// next star matrix
	CPoint star ;			// star matrix
	CPoint steal_corner ;	// stell corner matrix
	CPoint steal_all ;		// stell all matrix
	int rt = COM_ERROR ;	// return balue
	int i ;					// loop counter
	int val ;

	// init
	determ_put->x = 0 ;
	determ_put->y = 0 ;
	::memset(&corner, 0, sizeof(corner)) ;
	::memset(&nstar, 0, sizeof(nstar)) ;
	::memset(&star, 0, sizeof(star)) ;
	::memset(&steal_corner, 0, sizeof(steal_corner)) ;
	::memset(&steal_all, 0, sizeof(steal_all)) ;

	if(turn_cnt <= 2){	// first put location at random
		val = rand() ;
		val %= put_num ;
		::memcpy(determ_put, &put[val], sizeof(CPoint)) ;
	}else{
		// any judge case is put here....from future
		for(i=0;i<put_num;i++){

			// is all steal?
			if(AllSteal(put[i], EMY) == STEAL){
				::memcpy(determ_put, &put[i], sizeof(CPoint)) ;
				break ;
			}

			// less than, save proc anyway put matrix
			if(Corner(put[i]) == COM_NORMAL){
				// put corner?
				if(corner.x == 0){
					::memcpy(determ_put, &put[i], sizeof(CPoint)) ;
				}
			}else if(AllSteal(put[i], ENEMY) == STEAL){
				// steal all from enemy
				if(steal_all.x == 0){
					::memcpy(&steal_all, &put[i], sizeof(CPoint)) ;
				}
			}else if(StealCorner(put[i]) == STEAL){
				// steal corner from enemy
				if(steal_corner.x == 0){
					::memcpy(&steal_corner, &put[i], sizeof(CPoint)) ;
				}
			}else if(Star(put[i]) == STAR){
				// put matrix is star
				if(star.x == 0){
					::memcpy(&star, &put[i], sizeof(CPoint)) ;
				}
			}else if(StarNear(put[i]) == NSTAR){
				// put matrix is next star
				if(nstar.x == 0){
					::memcpy(&nstar, &put[i], sizeof(CPoint)) ;
				}
			}else{
				// maby no problem....
				if(determ_put->x == 0){
					::memcpy(determ_put, &put[i], sizeof(CPoint)) ;
				}
			}
		}

		// init return value
		rt = COM_NORMAL ;

		if(determ_put->x != 0){
			// found put stone
			;
		}else if(corner.x != 0){
			// corener
			::memcpy(determ_put, &corner, sizeof(CPoint)) ;
		}else if(star.x != 0){
			// star
			::memcpy(determ_put, &star, sizeof(CPoint)) ;
		}else if(nstar.x != 0){
			// next star
			::memcpy(determ_put, &nstar, sizeof(CPoint)) ;
		}else if(steal_corner.x != 0){
			// steal corner
			::memcpy(determ_put, &steal_corner, sizeof(CPoint)) ;
		}else if(steal_all.x != 0){
			// steal all
			::memcpy(determ_put, &steal_all, sizeof(CPoint)) ;
		}else{
			// not found put stone
			rt = COM_ERROR ;
		}
	}

	return COM_NORMAL ;
}

// +-+-+-+- judge corner +-+-+-+-
// return == COM_NORMAL	put corner
// return != COM_NORMAL	no put corner
// CPoint	 z			COM put matrix(IN)
int CComProc::Corner(CPoint z)
{	
	int rt = COM_ERROR ;	// return value

	if( (z.x == LINE_MIN || z.x == LINE_MAX) &&
		(z.y == LINE_MIN || z.y == LINE_MAX) ){
		rt = COM_NORMAL ;
	}

	return rt ;
}

// +-+-+-+- judge pingpoint corner +-+-+-+-
// return == COM_NORMAL	put corner
// return != COM_NORMAL	no put corner
// int		clr			wanna put color(IN)
int CComProc::CornerPP(int clr)
{	
	CPoint loop ;	// loop counter
	CPoint val ;	// matrix use from judge
	int corner[2] ;	// defined MAX,MIN
	int rt = COM_ERROR ;	// return value

	corner[0] = LINE_MIN ;
	corner[1] = LINE_MAX ;

	for(loop.y=0;(loop.y < 2) && (rt != PUT_NORMAL);loop.y++){
		for(loop.x=0;loop.x<2;loop.x++){
			val.x = corner[loop.x] ;
			val.y = corner[loop.y] ;
			rt = PutCheck(val, clr, CHK_ON, NULL) ;
			if(rt == PUT_NORMAL){
				// put corner
				break ;
			}
		}
	}

	return rt ;
}

// +-+-+-+- judge star +-+-+-+-
// return == COM_NORMAL		matrix is star
// return != COM_NORMAL		matrix is not star
// CPoint	z			COM put matrix(IN)
int CComProc::Star(CPoint z)
{
	int rt = NOT_STAR ;	// return value

	if((z.x == LINE_MIN+1 || z.x == LINE_MAX-1) &&
		(z.y == LINE_MIN+1 || z.y == LINE_MAX-1)){
		rt = STAR ;
	}else{
		rt = NOT_STAR ;
	}

	return rt ;
}

// +-+-+-+- judge next star +-+-+-+-
// return == COM_NORMAL		matrix is next star
// return != COM_NORMAL		matrix is not next star
// CPoint	z			COM put matrix(IN)
int CComProc::StarNear(CPoint z)
{
	int rt = NOT_NSTAR ;	// return value

	if( ((z.x == LINE_MIN || z.x == LINE_MAX) &&
		(z.y == LINE_MIN+1 || z.y == LINE_MAX-1)) ||
		((z.x == LINE_MIN+1 || z.x == LINE_MAX-1) &&
		(z.y == LINE_MIN || z.y == LINE_MAX)) ){
		rt = NSTAR ;
	}else{
		rt = NOT_NSTAR ;
	}

	return rt ;
}

// +-+-+-+- all steal? +-+-+-+-
// return == COM_NORMAL	all steal
// return != COM_NORMAL	not all steal
// CPoint	z			COM put matrix(IN)
// int		flg			which check object color(IN)
int CComProc::AllSteal(CPoint z, int flg)
{
	int rt_steal = NOT_STEAL ;	// is all steal??
#if ALLSTEAL_USE	// strange rogic 2 ENEMY
	int rt = COM_ERROR ;		// return value
	int enemy_clr = BLACK ;		// enemy color
	int put_stone[ORIENT_MAX] ;	// overturn number
	int black_num ;				// black color
	int white_num ;				// white color
	CPoint loop ;				// loop counter
	int FTable[LINE_MAX][LINE_MAX] ;	// put stone table is in

	// init
	::memset(&put_stone, 0, sizeof(put_stone)) ;
	::memset(&FTable, 0, sizeof(FTable)) ;
	::memcpy(Table, OrgTable, sizeof(Table)) ;

	// get enemy color
	if(color == BLACK){
		enemy_clr = WHITE ;
	}else{
		enemy_clr = BLACK ;
	}

	// try put stone
	PutCheck(z, color, PUT_ON, put_stone) ;
	PutStone(z, put_stone, color) ;
	::memcpy(FTable, Table, sizeof(Table)) ;

	if(flg == EMY){
		// all steal??
		CntStone(&black_num, &white_num) ;
		if( enemy_clr == WHITE && white_num == 0 ||
			enemy_clr == BLACK && black_num == 0){
			rt_steal = STEAL ;
		}
	}else{
		// comprise put matrix from enemy
		for(loop.x=1; (loop.x<=LINE_MAX) && (rt_steal != STEAL);loop.x++){
			for(loop.y=1; loop.y<=LINE_MAX;loop.y++){
				::memcpy(Table, FTable, sizeof(Table)) ;
				rt = PutCheck(loop, enemy_clr, PUT_ON, put_stone) ;
				// found put matrix
				if(rt == PUT_NORMAL){
					// try put stone
					PutStone(loop, put_stone, enemy_clr) ;

					// when put stone is me?
					CntStone(&black_num, &white_num) ;
					if(color == BLACK && black_num == 0 ||
						color == WHITE && white_num == 0){
						// all steal
						rt_steal = STEAL ;
						break ;
					}
				}
			}
		}
	}

	// feedback is use table
	::memcpy(Table, OrgTable, sizeof(Table)) ;

#endif
	return rt_steal ;
}

// move left up
// return == COM_NORMAL		stone is put
// return != COM_NORMAL		stone is not put
// CPoint	* z			COM put matrix(OUT)
int CComProc::LeftUp(CPoint * z)
{
	CPoint loop ;			// loop counter
	int rt	= COM_ERROR ;	// return value

	// init
	::memcpy(Table, OrgTable, sizeof(Table)) ;

	for(loop.y=1; loop.y<=LINE_MAX;loop.y++){
		for(loop.x=1; loop.x<=LINE_MAX;loop.x++){
			rt = PutCheck(loop, color, CHK_ON, NULL) ;
			if(rt == PUT_NORMAL){
				// found put matrix
				::memcpy(z, &loop, sizeof(CPoint)) ;
				return COM_NORMAL ;
			}
		}
	}

	return COM_ERROR ;
}

// +-+-+-+- get center +-+-+-+-
// return == COM_NORMAL		stone is put
// return != COM_NORMAL		stone is not put
// CPoint	put				possible put matrix(OUT)
// int		put_num			possible put number(OUT)
int CComProc::Center(CPoint put[MAS_MAX], int * put_num)
{
	CPoint	loop ;				// loop couter
	CPoint	zz ;				// matrix
	CPoint	amp ;				// amplifier
	CPoint	sort_pos ;			// work matrix of sort proc
	int		num_wk = 0 ;		// possible put number
	int		rt = COM_ERROR ;	// return value
	int		i,j ;				// bubble sort counter
	int		value1,value2 ;		// compare value
	
	// init
	amp.x = -1 ;
	amp.y = -1 ;
	::memcpy(Table, OrgTable, sizeof(Table)) ;

	for(loop.y=1, zz.y=(LINE_MAX/2);loop.y<=LINE_MAX;loop.y++){
		for(loop.x=1, zz.x=(LINE_MAX/2);loop.x<=LINE_MAX;loop.x++){
			rt = PutCheck(zz, color, CHK_ON, NULL) ;
			if(rt == PUT_NORMAL){
				// found put matrix
				put[num_wk].x = zz.x ;
				put[num_wk].y = zz.y ;
				num_wk++ ;
//				TRACE3("sort before CNT No=%d x=%d y=%d\n",num_wk,zz.x,zz.y) ;
			}
			amp.x*= -1 ;
			zz.x += amp.x * loop.x ;
		}
		amp.y *= -1 ;
		zz.y += amp.y * loop.y ;
	}

	// sort found matrix most center is top
	if(num_wk > 0){
		for(i=0;i<num_wk;i++){
			for(j=i+1;j<num_wk;j++){
				value1 = abs(4-put[i].x) + abs(4-put[i].y) ;
				value2 = abs(4-put[j].x) + abs(4-put[j].y) ;

				if(value1 > value2){
					::memcpy(&sort_pos, &put[i], sizeof(CPoint)) ;
					::memcpy(&put[i], &put[j], sizeof(CPoint)) ;
					::memcpy(&put[j], &sort_pos, sizeof(CPoint)) ;
				}
			}
		}
//		for(i=0;i<num_wk;i++){
//			TRACE3("sort after CNT No=%d x=%d y=%d\n",i+1,put[i].x,put[i].y) ;
//		}
		*put_num = num_wk ;
		rt = COM_NORMAL ;
	}else{
		rt = COM_ERROR ;
	}

	return rt ;
}

// +-+-+- a lot of overturn +-+-+-
// return == COM_NORMAL		stone is put
// return != COM_NORMAL		stone is not put
// CPoint	put				possible stone matrix(OUT)
// int		put_num			possible stone number(OUT)
int CComProc::GetMax(CPoint put[MAS_MAX], int * put_num)
{
	CPoint loop ;				// counter
	int put_stone[ORIENT_MAX] ;	// ouverturn number
	int rt = COM_ERROR ;		// return value
	int num_cnt[MAS_MAX] ;		// total overturn number1
	int num = 0 ;				// total overturn number2
	int num_wk = 0 ;			// possible put stone number
	CPoint sort_pos ;			// work matrix from sort
	int sort_num ;				// work number from sort
	int loop_cnt ;				// loop counter from overturn count
	int i,j ;					// loop counter

	// init
	::memset(&put_stone, 0, sizeof(put_stone)) ;
	::memset(&num_cnt, 0, sizeof(num_cnt)) ;
	::memcpy(Table, OrgTable, sizeof(Table)) ;

	for(loop.y=1; loop.y<=LINE_MAX;loop.y++){
		for(loop.x=1; loop.x<=LINE_MAX;loop.x++){
			// original table copy 2 work table
//			::memcpy(Table,OrgTable,sizeof(Table)) ;
			::memset(&put_stone, 0, sizeof(put_stone)) ;

			rt = PutCheck(loop,color, PUT_ON, put_stone) ;
			if(rt == PUT_NORMAL){
				// found put stone
				put[num_wk].x = loop.x ;
				put[num_wk].y = loop.y ;

				// count overturn stone
				num = 0 ;
				for(loop_cnt=0;loop_cnt<ORIENT_MAX;loop_cnt++){
					num += put_stone[loop_cnt] ;
				}
				num_cnt[num_wk] = num ;
				num_wk++ ;
				TRACE2("sort before MAX No=%d num=%d\n",num_wk,num) ;
			}
		}
	}

	// sort
	if(num_wk > 0){
		for(i=0;i<num_wk;i++){
			for(j=i+1;j<num_wk;j++){
				if(num_cnt[i] < num_cnt[j]){
					::memcpy(&sort_pos, &put[i], sizeof(CPoint)) ;
					::memcpy(&put[i], &put[j], sizeof(CPoint)) ;
					::memcpy(&put[j], &sort_pos, sizeof(CPoint)) ;
					sort_num = num_cnt[i] ;
					num_cnt[i] = num_cnt[j] ;
					num_cnt[j] = sort_num ;
				}
			}
		}
		for(i=0;i<num_wk;i++){
			TRACE2("sort after MAX No=%d num=%d\n",i+1,num_cnt[i]) ;
		}
		// get normal
		*put_num = num_wk ;
		rt = COM_NORMAL ;
	}else{
		rt = COM_ERROR ;
	}

	return rt ;
}

// +-+-+- little overturn +-+-+-
// return == COM_NORMAL		stone is put
// return != COM_NORMAL		stone is not put
// CPoint	put				possible put mattrix(OUT)
// int		put_num			possible put number(OUT)
int CComProc::GetMin(CPoint put[MAS_MAX], int * put_num)
{
	CPoint loop ;				// loop couter
	int put_stone[ORIENT_MAX] ;	// overturn number
	int rt = COM_ERROR ;		// return value
	int num_cnt[MAS_MAX] ;		// total overturn number
	int num = 0 ;				// total overturn number
	int num_wk = 0 ;			// number of possible put matrix
	CPoint sort_pos ;			// work matrix from sort
	int sort_num ;				// work number from sort
	int loop_cnt ;				// loop counter from overturn count
	int i,j ;					// loop counter

	// init
	::memset(&put_stone, 0, sizeof(put_stone)) ;
	::memset(&num_cnt, 0, sizeof(num_cnt)) ;
	::memcpy(Table, OrgTable, sizeof(Table)) ;

	for(loop.y=1; loop.y<=LINE_MAX;loop.y++){
		for(loop.x=1; loop.x<=LINE_MAX;loop.x++){
			// orginal table copy 2 work table
//			::memcpy(Table,OrgTable,sizeof(Table)) ;
			::memset(&put_stone, 0, sizeof(put_stone)) ;

			rt = PutCheck(loop, color, PUT_ON, put_stone) ;
			if(rt == PUT_NORMAL){
				// found put matrix
				put[num_wk].x = loop.x ;
				put[num_wk].y = loop.y ;

				// count of overturn stone
				num = 0 ;
				for(loop_cnt=0;loop_cnt<ORIENT_MAX;loop_cnt++){
					num += put_stone[loop_cnt] ;
				}
				num_cnt[num_wk] = num ;
				num_wk++ ;
				TRACE2("sort before MIN No=%d num=%d",num_wk,num) ;
				TRACE2("  MIN: pos x=%d y=%d\n",loop.x,loop.y) ;
			}
		}
	}

	// sort
	if(num_wk > 0){
		for(i=0;i<num_wk;i++){
			for(j=i+1;j<num_wk;j++){
				if(num_cnt[i] > num_cnt[j]){
					::memcpy(&sort_pos, &put[i], sizeof(CPoint)) ;
					::memcpy(&put[i], &put[j], sizeof(CPoint)) ;
					::memcpy(&put[j], &sort_pos, sizeof(CPoint)) ;
					sort_num = num_cnt[i] ;
					num_cnt[i] = num_cnt[j] ;
					num_cnt[j] = sort_num ;
				}
			}
		}
		for(i=0;i<num_wk;i++){
			TRACE2("sort after MIN No=%d num=%d\n",i+1,num_cnt[i]) ;
		}
		// get normal
		*put_num = num_wk ;
		rt = COM_NORMAL ;
	}else{
		rt = COM_ERROR ;
	}

	return rt ;
}

// +-+-+- open theory +-+-+-
// return == COM_NORMAL		stone is put
// return != COM_NORMAL		stone is not put
// CPoint	put				possible put matrix(OUT)
// int		put_num			possible put number(OUT)
int CComProc::GetKaiho(CPoint put[MAS_MAX], int * put_num)
{
	CPoint loop ;				// loop counter
	int put_stone[ORIENT_MAX] ;	// overturn number
	int rt = COM_ERROR ;		// return value
	int num_cnt[MAS_MAX] ;		// total overturn number
	int num = 0 ;				// total overturn number
	int num_wk = 0 ;			// number of possible put matrix
	CPoint sort_pos ;			// work matrix from sort
	int sort_num ;				// work number from sort
	int i,j ;					// loop counter

	// init
	::memset(&put_stone, 0, sizeof(put_stone)) ;
	::memset(&num_cnt, 0, sizeof(num_cnt)) ;
	::memcpy(Table, OrgTable, sizeof(Table)) ;


	for(loop.y=1; loop.y<=LINE_MAX;loop.y++){
		for(loop.x=1; loop.x<=LINE_MAX;loop.x++){
			// orginal table copy 2 work table
			// ここを実行するとアプリが固まる？！
//			::memcpy(Table,OrgTable,sizeof(Table)) ;
			::memset(&put_stone, 0, sizeof(put_stone)) ;

			rt = PutCheck(loop, color, PUT_ON, put_stone) ;
			if(rt == PUT_NORMAL){
				// found put matrix
				put[num_wk].x = loop.x ;
				put[num_wk].y = loop.y ;

				// open theory
				num = Kaiho(loop, put_stone) ;
				num_cnt[num_wk] = num ;
				num_wk++ ;
				if(num < 0){
					// if put nuber is none then "num=0"
					TRACE3("Kaiho Bag num=%d X=%d Y=%d\n",num,loop.x,loop.y) ;
					// repeat from Debug
					Kaiho(loop, put_stone) ;
				}
			}
		}
	}
	TRACE2("sort before KAI No=%d num=%d\n",num_wk+1,num) ;

	// sort
	if(num_wk > 0){
		for(i=0;i<num_wk;i++){
			for(j=i+1;j<num_wk;j++){
				if(num_cnt[i] > num_cnt[j]){
					::memcpy(&sort_pos, &put[i], sizeof(CPoint)) ;
					::memcpy(&put[i], &put[j], sizeof(CPoint)) ;
					::memcpy(&put[j], &sort_pos, sizeof(CPoint)) ;
					sort_num = num_cnt[i] ;
					num_cnt[i] = num_cnt[j] ;
					num_cnt[j] = sort_num ;
				}
			}
		}
		for(i=0;i<num_wk;i++){
			TRACE2("sort after KAI No=%d num=%d\n",i+1,num) ;
		}
		// get normal
		*put_num = num_wk ;
		rt = COM_NORMAL ;
	}else{
		rt = COM_ERROR ;
	}

	return rt ;
}

// +-+-+- open range +-+-+-
// return > 0	overturn number
// CPoint	z	COM put matrix(IN)
// int put_stone orientation and number of stone(OUT)
int CComProc::Kaiho(CPoint z, int put_stone[ORIENT_MAX])
{
	int X = z.x-1 ;		// matrix(X)
	int Y = z.y-1 ;		// matirx(Y)
	int num = 0 ;		// open range number
	int count = 0 ;		// overturn number of one way
	int orient = 0 ;	// orientation
	int orient_kaiho= 0 ;	// orientation of open theory

	// init
	::memcpy(Table, OrgTable, sizeof(Table)) ;

	// get put number
	PutCheck(z, color, PUT_ON, put_stone) ;

	// calculation in open range from put now matrix
	num = KaihoSub(X, Y) ;

	for(orient=0; orient<ORIENT_MAX; orient++){
		for(count=1; count<=put_stone[orient];count++){
			X = z.x-1 ;
			Y = z.y-1 ;
			switch(orient+1){
			case LEFT :
				X -= count ;
				break ;

			case RIGHT :
				X += count ;
				break ;

			case UP :
				Y -= count ;
				break ;

			case DOWN :
				Y += count ;
				break ;

			case LEFT_UP :
				X -= count ;
				Y -= count ;
				break ;

			case LEFT_DOWN :
				X -= count ;
				Y += count ;
				break ;

			case RIGHT_UP :
				X += count ;
				Y -= count ;
				break ;

			case RIGHT_DOWN :
				X += count ;
				Y += count ;
				break ;

			default :
				break ;
			}

			// calculation put each matrix
			num += KaihoSub(X, Y) ;
		}
	}

	return num ;
}

// +-+-+- calculation open range +-+-+-
// return > 0	overturn number
// int	x		COM put matrix(X) (IN)
// int	y		COM put matrix(Y) (IN)
int CComProc::KaihoSub(int x, int y)
{
	int orient = 0 ;	// orientation
	int num = 0 ;		// number
	int i,j ;			// loop counter

	x -- ;
	y -- ;

	// calculation put each matrix
	for(i=0;i<3;i++){
		for(j=0;j<3;j++){
			if(i == 1 && j == 1){
				// put matrix in now
				continue ;
			}else if(x < 0 || y < 0){
				// outside in table
				continue ;
			}else if(Table[y+i][x+j] == 0){
				// empty way of put stone
				num++ ;
			}
		}
	}

	return num ;
}

// try put stone in get matrix,and think enemy turn
// +-+-+- steal corner? +-+-+-
// return == 0		steal corner
// return != 0		not steal corner
// CPoint	z		COM put matrix(IN)
int CComProc::StealCorner(CPoint z)
{
	int enmy_clr = 0 ;			// enemy color
	int rt = COM_ERROR ;		// return value
	int put_stone[ORIENT_MAX] ;	// overturn number

	// init
	::memset(&put_stone, 0, sizeof(put_stone)) ;
	::memcpy(Table, OrgTable, sizeof(Table)) ;

	// get enemy color
	if(color== BLACK){
		enmy_clr = WHITE ;
	}else{
		enmy_clr = BLACK ;
	}

	// try put stone
	PutCheck(z, color, PUT_ON, put_stone) ;
	PutStone(z, put_stone, color) ;

	// steal corner?
	rt = CornerPP(enmy_clr) ;
	if(rt == PUT_NORMAL){
		// steal
		rt = STEAL ;
	}else{
		// not steal
		rt = NOT_STEAL ;
	}
	
	return rt ;
}

// +-+-+- judge get straight +-+-+-
// return == COM_NORMAL		stone is put
// return != COM_NORMAL		stone is not put
// CPoint	* z			COM put stone matrix(OUT)
int CComProc::Straight(CPoint * z)
{
	int rt = COM_ERROR ;	// return value

	return rt ;
}
