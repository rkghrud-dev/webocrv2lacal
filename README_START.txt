WEBOCRV2_LOCAL

실행:
  RUN.BAT

중지:
  STOP.BAT

RUN.BAT을 실행하면 webocrcludev2 로컬 서버가 켜지고 브라우저가 자동으로 열립니다.
기본 주소는 http://localhost:5556/index.html 입니다.

주요 폴더:
  webocrcludev2\data\exports       생성된 export / 업로드용 파일
  webocrcludev2\data\jobs          실행 작업 로그
  webocrcludev2\data\seeds         web seed 파일
  webocrcludev2\data\uploads       업로드 원본/중간 파일
  webocrcludev2\data\market_keys   웹에서 관리하는 마켓 키
  webocrcludev2\data\desktop_key   C# 업로드 모듈이 읽는 키/상태 파일
  ProductManager\data              상품 DB

이 폴더 안에서 작업하면 새로 만들어지는 파일도 webocrcludev2\data 아래에 정리됩니다.
