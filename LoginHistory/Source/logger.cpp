#define INI_FILE_NAME	"set.ini"
#define LOG_FILE_NAME	"C:\\temp\\Alive.log"
#define FILE_NAME		"FileName"
#define CYCLE			"Cycle"
#define WARM			" !"
#define COMENT			";"

#define UPDATE_CYC		(60)	// min
#define STR_BUFF		(30)
#define SBCS			(0)
#define DBCS			(1)
#define	NORMAL			(0)
#define ERROR			(-1)
#define TRUE			(1)
#define	FALSE			(0)
#define	MAX_MON			(12)
#define	MAX_DAY			(31)
#define	MAX_HOUR		(24)
#define	MAX_MIN			(60)
#define	MAX_SEC			(60)
#define	DIFFER			(10)
#define MOUNTH_OFFSET	(1)
#define YEAR_OFFSET		(1900)

void LoadIniFile(char fname[STR_BUFF], int * cyc) ;
void CreateFile(char fname[STR_BUFF], tm * time) ;
void GetLastUpDate(FILE * fp, tm * time) ;
int  GetTimeString(char time_str[STR_BUFF], tm * time) ;
int  CheckDate(tm * t_time) ;
int  CheckUpDateTime(tm * o_time, tm * n_time, int cyc) ;

int _tmain(int argc, _TCHAR* argv[])
{
	FILE * fp ;						// file pointer
    tm * o_time = NULL ;			// old time(sec)
    tm * n_time = NULL ;			// new time(sec)
	time_t now = 0 ;				// now time
	int cycle  = UPDATE_CYC ;		// update cycle
	char buff[STR_BUFF] ;			// get data buffer
	char file_name[STR_BUFF] ;		// set log file name

	memset(buff, 0, sizeof(buff)) ;
	memset(file_name, 0, sizeof(file_name)) ;
	strcpy(file_name, LOG_FILE_NAME) ;

	// load ini file
	LoadIniFile(file_name, &cycle) ;

	// Get time as long integer.
	time( &now ) ;
    n_time = localtime( &now ) ;

	// opend update file
	fp = fopen(file_name, "r+") ;
	if(fp == NULL){
		// create new file
		CreateFile(file_name, n_time) ;
	}else{
		// write file
		char time_str[STR_BUFF];
		memset(time_str, 0, sizeof(time_str));

		GetTimeString(time_str, n_time);
		o_time = (tm *)malloc(sizeof(tm));
		GetLastUpDate(fp, o_time) ;
		if(CheckUpDateTime(o_time, n_time, cycle) == FALSE){
			strcat(time_str,WARM);
		}
		free(o_time);

		fseek(fp,0,SEEK_END);
		fprintf(fp,"%s\n",time_str);
	}
	
	return NORMAL ;
}


void LoadIniFile(char * fname, int * cyc)
{
	FILE *fp ;
	char buff[STR_BUFF] ;

	memset(buff, 0 ,sizeof(buff)) ;

	fp = fopen(INI_FILE_NAME, "r") ;
	if(fp != NULL){
		while(fgets(buff, sizeof(buff), fp)){
			if(strncmp(buff, COMENT, strlen(COMENT)) == 0){
				continue;
			}else if(strncmp(buff, FILE_NAME, strlen(FILE_NAME)) == 0){
				sscanf(buff, "FileName=%s", fname) ;
			}else if(strncmp(buff, CYCLE, strlen(CYCLE)) == 0){
				sscanf(buff, "Cycle=%d", cyc) ;
			}
		}
		fclose(fp) ;
	}
}

void CreateFile(char * fname, tm * time)
{
	FILE * fp ;
	char time_str[STR_BUFF];

	memset(time_str, 0, sizeof(time_str));

	GetTimeString(time_str, time);
	fp = fopen(fname, "w") ;
	if(fp != NULL){
		fprintf(fp,"%s\n",time_str);
		fclose(fp) ;
	}
}

int GetTimeString(char * time_str, tm *time)
{
	sprintf(time_str, "%04d.%02d.%02d_%02d:%02d:%02d",
		time->tm_year + YEAR_OFFSET,
		time->tm_mon + MOUNTH_OFFSET,
		time->tm_mday,
		time->tm_hour,
		time->tm_min,
		time->tm_sec) ;

	return CheckDate(time) ;
}

void GetLastUpDate(FILE * fp, tm * time)
{
	tm tt;
	int rt;
	char buff[STR_BUFF] ;

	memset(buff, 0, sizeof(buff));
	memset(&tt, 0, sizeof(tt));

	fseek(fp,-(STR_BUFF),SEEK_END);
	while(fgets(buff, sizeof(buff), fp)){
		sscanf(buff, "%d.%02d.%02d_%02d:%02d:%02d",
			&tt.tm_year,
			&tt.tm_mon,
			&tt.tm_mday,
			&tt.tm_hour,
			&tt.tm_min,
			&tt.tm_sec) ;
		tt.tm_mon -= MOUNTH_OFFSET ;
		tt.tm_year -= YEAR_OFFSET ;

		rt = CheckDate(&tt) ;
		if(rt == NORMAL){
			*time = tt ;
		}
	}
}

int	CheckDate(tm *t_time)
{
	tm tmp_time ;
	int rt = ERROR ;

	memcpy(&tmp_time, t_time, sizeof(tmp_time));
	tmp_time.tm_year =+ YEAR_OFFSET;
	tmp_time.tm_mon =+ MOUNTH_OFFSET;

	if(0 < tmp_time.tm_year &&
		0 < tmp_time.tm_mon && tmp_time.tm_mon <= MAX_MON &&
		0 < tmp_time.tm_mday && tmp_time.tm_mday <= MAX_DAY &&
		0 <= tmp_time.tm_hour && tmp_time.tm_hour < MAX_HOUR &&
		0 <= tmp_time.tm_min && tmp_time.tm_min < MAX_MIN &&
		0 <= tmp_time.tm_sec && tmp_time.tm_sec < MAX_SEC )
		rt = NORMAL ;

	return rt;
}

int CheckUpDateTime(tm * o_time, tm * n_time, int cyc)
{
	tm wk_time;
	int rt = TRUE ;
	int new_time ;
	int old_time ;
	int diff_time;

	memcpy(&wk_time,n_time,sizeof(tm));
	// check time
	new_time = mktime(n_time) ;
	old_time = mktime(o_time) ;

	diff_time = new_time - (old_time + cyc * MAX_SEC);
	abs(diff_time);
	if(diff_time > DIFFER){
		// time lag is DIFFER(10) sec over
		rt = FALSE ;
	}

	return rt ;
}

