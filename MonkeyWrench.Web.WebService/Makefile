SOURCES = \
	$(wildcard *.cs)	\
	$(wildcard */*.cs)

TOPDIR = ..

LIBRARY = MonkeyWrench.Web.WebService.dll
OUT_DIR = $(TOPDIR)/class/lib
OUT = $(OUT_DIR)/$(LIBRARY)

BUILDABLE_REFS = \
	$(OUT_DIR)/MonkeyWrench.dll	\
	$(OUT_DIR)/MonkeyWrench.DataClasses.dll	\
	$(OUT_DIR)/MonkeyWrench.Database.dll

REFERENCES = \
	$(foreach ref,$(BUILDABLE_REFS),-r:$(ref))	\
	-r:Npgsql.dll	\
	-r:System.Data.dll	\
	-r:System.Web.dll	\
	-r:System.Web.Services.dll

SOURCES = \
	$(wildcard *.cs)

$(OUT): $(SOURCES) $(BUILDABLE_REFS)
	MONO_PATH=$(OUT_DIR):$(MONO_PATH) gmcs -target:library -debug:full -out:$@ $(SOURCES) $(REFERENCES)

all: $(OUT)

clean:
	rm -f $(OUT)*
	rm -f bin/*.dll bin/*.mdb bin/*.pdb

install: all
	mkdir -p bin
	cp -pu $(OUT)* bin
	cp -pu $(OUT_DIR)/Npgsql.dll* bin
	cp -pu $(OUT_DIR)/MonkeyWrench.dll* bin
	cp -pu $(OUT_DIR)/MonkeyWrench.DataClasses.dll* bin
	cp -pu $(OUT_DIR)/MonkeyWrench.Database.dll* bin

dataclasses:
	$(MAKE) -C $(TOPDIR)/MonkeyWrench.DataClasses
	$(MAKE) dependencies

wsdl: WebServices.wsdl

WebServices.wsdl: all
	curl http://localhost:8123/WebServices/WebServices.asmx?wsdl > WebServices.wsdl -m  5

