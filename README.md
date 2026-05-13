# MD Pad WV2

Windows 11 스타일의 단일 창 Markdown 편집기입니다.

## 기능

- 다중 탭 문서
- 편집 / 미리보기 전환
- GitHub Markdown CSS 기반 미리보기
- GFM 테이블, 체크리스트, 코드블럭, 이미지 렌더링
- 코드블럭 복사 버튼
- 체크리스트 미리보기 클릭 토글
- `Ctrl+F` 검색, `Ctrl+S` 저장, `Ctrl+O` 열기, `Ctrl+N` 새 탭
- `.md`, `.markdown`, `.txt` 드래그앤드롭 열기
- 동일 서브넷 MD Pad 인스턴스 간 문서 전송

## 빌드

```powershell
dotnet build .\MdPadWv2.sln -c Release
```

## 패키징

```powershell
dotnet publish .\MdPad.Wpf\MdPad.Wpf.csproj -c Release -r win-x64 --self-contained true -o .\release\app
& 'C:\Program Files (x86)\NSIS\makensis.exe' .\installer.nsi
```
